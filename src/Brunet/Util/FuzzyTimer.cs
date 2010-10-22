/*
Copyright (C) 2008  P. Oscar Boykin <boykin@pobox.com>, University of Florida

This program is free software; you can redistribute it and/or
modify it under the terms of the GNU General Public License
as published by the Free Software Foundation; either version 2
of the License, or (at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program; if not, write to the Free Software
Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.
*/

using System;
using System.Threading;
using System.Collections.Generic;
#if BRUNET_NUNIT
using NUnit.Framework;
#endif

using Brunet.Collections;
using Brunet.Concurrent;

namespace Brunet.Util
{

/** Represents a scheduled event to happen during some interval
 */
public class FuzzyEvent : Interval<DateTime> {

  protected int _has_run;
  public bool HasRun { get { return _has_run == 1; } }
  protected readonly System.Action<DateTime> _todo;

  public FuzzyEvent(System.Action<DateTime> todo, DateTime start, DateTime end)
#if BRUNET_SIMULATOR
       : base(end, false, end, false, Comparer<DateTime>.Default) {
#else
       : base(start, false, end, false, Comparer<DateTime>.Default) {
#endif
    _todo = todo;
    _has_run = 0;
  }

  /** try to cancel without running
   * @return true if we could cancel, false if we have already run
   */
  public virtual bool TryCancel() {
    return (0 == Interlocked.Exchange(ref _has_run, 1));
  }

  /** try to run the event
   * @return true if we could run, false if we have already run or canceled
   */
  public virtual bool TryRun(DateTime now) {
    if( 0 == Interlocked.Exchange(ref _has_run, 1) ) {
      _todo(now);
      return true; 
    }
    else {
      return false;
    }
  }
  
}

/**
 * Use this if you want to repeat an operation at a fixed interval
 */
public class RepeatingFuzzyEvent : FuzzyEvent {

  protected readonly TimeSpan _interval;

  protected class Flag { 
    protected int _val;
    public bool Value {
      get { return _val == 1; }
    }
    public Flag() {
      _val = 0;
    }
    public bool TrySet() {
      return (0 == Interlocked.Exchange(ref _val, 1)); 
    }
  }
  protected readonly Flag _flag;
    
  /**
   * @param todo the Action to execute
   * @param start the start of the acceptable interval to run
   * @param end the end of the acceptable interval to run
   * @param waitinterval the interval of time to wait until the next run
   * 
   * This creates an infinite series of intervals [s,e], [s+wi,e+wi],
   * [s+2wi, e+2wi],...
   *
   * To stop the infinite sequence, call the TryCancel() method.
   */
  public RepeatingFuzzyEvent(System.Action<DateTime> todo, DateTime start, DateTime end, TimeSpan waitinterval)
    : base(todo, start, end) {
    _interval = waitinterval;
    _flag = new Flag();
  }

  protected RepeatingFuzzyEvent(System.Action<DateTime> todo, DateTime start, DateTime end, TimeSpan interval, Flag flag)
    : base(todo, start, end) {
    _interval = interval;
    _flag = flag;
  }
  public override bool TryCancel() {
    return _flag.TrySet();
  }
  //Run, and reschedule
  public override bool TryRun(DateTime now) {
    if( _flag.Value == false ) {
      //We still have not canceled:
      _todo(now);
      //We have to make a new event because Interval<DateTime> has readonly
      //fields.  We pass the flag variable so share one big cancel variable
      var fe = new RepeatingFuzzyEvent(_todo, Start + _interval, End + _interval, _interval, _flag);
      FuzzyTimer.Instance.Schedule(fe);
      return true;
    }
    else {
      return false;
    }
  }

}

/** A Latency aware timer
 * All scheduled actions happen in one internal background thread.
 */
public class FuzzyTimer : IDisposable {

#if !BRUNET_SIMULATOR
  readonly LFBlockingQueue<FuzzyEvent> _incoming_events;
  readonly Thread _timer_thread;
#endif
  protected static FuzzyTimer _singleton;
  //This is a singleton class
  public static FuzzyTimer Instance {
    get {
      if( _singleton != null ) {
        return _singleton;
      }
      
      //else we try to set the singleton value:
      FuzzyTimer new_val = new FuzzyTimer();
      FuzzyTimer old_val = Interlocked.CompareExchange<FuzzyTimer>(ref _singleton, new_val, null);
      if( old_val == null ) {
#if !BRUNET_SIMULATOR
        //We just created a new FuzzyTimer, so we have to start it:
        new_val._timer_thread.Start();
#endif
        return new_val;
      }
      else {
        //There was already a FuzzyTimer created:
        return old_val;
      }
    }
  }
    
  protected long _last_run;
  //When did we last run an event
  public DateTime LastRun {
    get {
      return new DateTime(Interlocked.Read(ref _last_run));
    }
  }

  protected FuzzyTimer() {
#if !BRUNET_SIMULATOR
    _incoming_events = new LFBlockingQueue<FuzzyEvent>(); 
    _timer_thread = new Thread(TimerThread);
    _timer_thread.IsBackground = true;
#endif
  }

  ~FuzzyTimer() {
    Dispose();
  }

  /**
   * Creates a new FuzzyEvent, Schedules it and returns it
   * @param todo the method to call
   * @param from_now_ms the TimeSpan from now to call the method in milliseconds
   * @param latency_ms the acceptable latency for this call in milliseconds
   */
  public FuzzyEvent DoAfter(System.Action<DateTime> todo,
                                      int from_now_ms, int latency_ms) {
    DateTime start = DateTime.UtcNow + new TimeSpan(0,0,0,0,from_now_ms);
    DateTime end = start + new TimeSpan(0,0,0,0,latency_ms);
    FuzzyEvent new_event = new FuzzyEvent(todo, start, end);
    Schedule(new_event);
    return new_event;
  }

  /** 
   * Creates a new FuzzyEvent, Schedules it and returns it
   * @param todo the method to call
   * @param from_now_ms the TimeSpan from now to call the method
   * @param latency_ms the acceptable latency for this call in milliseconds
   */
  public FuzzyEvent DoAt(System.Action<DateTime> todo, DateTime at,
                                   int latency_ms) {
    DateTime end = at + new TimeSpan(0,0,0,0,latency_ms);
    FuzzyEvent new_event = new FuzzyEvent(todo, at, end);
    Schedule(new_event);
    return new_event;
  }
  
  /** 
   * Creates a new FuzzyEvent, Schedules it and returns it
   * @param todo the method to call
   * @param period_ms how long to wait between runs
   * @param latency_ms the acceptable latency for this call in milliseconds
   */
  public RepeatingFuzzyEvent DoEvery(System.Action<DateTime> todo, int period_ms, int latency_ms) {
    TimeSpan waitinterval = new TimeSpan(0,0,0,0,period_ms);
    TimeSpan lat = new TimeSpan(0,0,0,0,latency_ms);
    
    DateTime start = DateTime.UtcNow + waitinterval;
    DateTime end = start + lat;
    RepeatingFuzzyEvent new_event = new RepeatingFuzzyEvent(todo, start, end, waitinterval);
    Schedule(new_event);
    return new_event;
  }

  public void Dispose() {
#if !BRUNET_SIMULATOR
    _incoming_events.Enqueue(null);
    if( Thread.CurrentThread != _timer_thread ) {
      _timer_thread.Join();
    }
#endif
  }

#if BRUNET_SIMULATOR
  // Simulator does not need a lock as it is single threaded
  Heap<FuzzyEvent> _events = new Heap<FuzzyEvent>();

  public long Minimum {
    get {
      if(_events.Count == 0) {
        return long.MaxValue;
      }
      return _events.Peek().Start.Ticks;
    }
  }

  public long Run() {
    DateTime now = DateTime.UtcNow;
    while(_events.Count > 0 && _events.Peek().Start <= now) {
      FuzzyEvent fe = _events.Pop();
      fe.TryRun(now);
    }
    return Minimum;
  }
#endif

  public void Schedule(FuzzyEvent e) {
#if BRUNET_SIMULATOR
    _events.Add(e);
#else
    _incoming_events.Enqueue(e);
#endif
  }

#if !BRUNET_SIMULATOR
  protected void TimerThread() {
    Heap<FuzzyEvent> events = new Heap<FuzzyEvent>();
    List<FuzzyEvent> next_todos = new List<FuzzyEvent>();
    Interval<DateTime> next_schedule_int = null;
    int wait_interval = -1;
    bool run = true;
    bool timedout;
    while(run) {
      FuzzyEvent fe = _incoming_events.Dequeue(wait_interval, out timedout); 
      if( !timedout ) {
        //We got a new event
        if( fe != null ) { events.Add(fe); }
        else {
          //Got a null event, that means stop:
          break;
        }
      }
      DateTime now = DateTime.UtcNow;
      /*
       * Since we've already been awakened, let's check to see if we can run:
       */
      if( next_schedule_int != null ) {
        if (next_schedule_int.CompareTo(now) <= 0 ) {
          //We are safe to go ahead and run:
          Interlocked.Exchange(ref _last_run, now.Ticks);
          foreach(FuzzyEvent feitem in next_todos) {
            try {
              feitem.TryRun(now);
            }
            catch(Exception x) {
              Console.WriteLine(x);
              //Something bad happened
            }
          }
          //Now reset and reschedule below:
          next_todos.Clear();
          next_schedule_int = null;
        }
        else {
          //It's not yet time to run.
        }
      }
      //Time to schedule the next wait:
      Interval<DateTime> overlap;
      do {
        if( events.Count > 0 ) {
          overlap = events.Peek();
          if( next_schedule_int != null ) {
            //We already have something scheduled:
            var new_overlap = next_schedule_int.Intersection(overlap);
            if( new_overlap == null ) {
              if( overlap.CompareTo( next_schedule_int ) <= 0 ) {
              /*
               * If there is no overlap, but next_schedule_int is after,
               * overlap, we need to reorder things:
               */
                //Put the next_todos back:
                var new_next = events.Pop();
                foreach(FuzzyEvent fev in next_todos) {
                  events.Add(fev); 
                }
                next_todos.Clear();
                next_todos.Add( new_next );
                next_schedule_int = new_next;
                overlap = new_next;
              }
              else {
                //we'll deal with overlap later:
                overlap = null;
              }
            }
            else {
              //There is an overlap
              //We can combine the old and new event:
              overlap = new_overlap;
              next_schedule_int = overlap;
              next_todos.Add( events.Pop() );
            }
          }
          else {
            //There was nothing scheduled:
            next_schedule_int = overlap;
            next_todos.Add( events.Pop() );
          }
        }
        else {
          overlap = null;
        }
      } while(overlap != null);

      if( next_schedule_int != null ) {
        //Wait as long as possible, we may be able to combine later:
        TimeSpan to_wait = next_schedule_int.End - now;
        wait_interval = (int)to_wait.TotalMilliseconds;
        if( wait_interval < 0 ) {
          //Well, we should be able to go ahead and run, so do it:
          wait_interval = 0;
        }
      }
      else {
        //Nothing to do, just wait for the next scheduled operation
        wait_interval = -1;
      }
    }
  }
#endif

}

#if BRUNET_NUNIT
[TestFixture]
public class FuzzyTimerTest {

  [Test]
  public void BasicTest() {
    FuzzyTimer ft = FuzzyTimer.Instance;
    //Check that the singleton is working:
    Assert.IsTrue( FuzzyTimer.Instance == FuzzyTimer.Instance, "singleton test");

    System.Random r = new System.Random();
    int TESTS = 1000;
    int MAX_WAIT = 5000;
    int MAX_LAT = 500;
    List<double> deltas = new List<double>();
    for(int i = 0; i < TESTS; i++) {
      int when = r.Next(1, MAX_WAIT); //Random time in next 5 seconds;
      int lat = r.Next(1,MAX_LAT); //Random interval;
      DateTime now = DateTime.UtcNow;
      DateTime when_dt = now + TimeSpan.FromMilliseconds(when);
      System.Action<DateTime> rt = delegate(DateTime d) {
        lock(deltas) {
          //Record the difference of when we should run
          deltas.Add(System.Math.Abs((d - when_dt).TotalMilliseconds)/((double)(lat)));
        }
      };
      ft.DoAfter(rt, when, lat);
    }
    Thread.Sleep(3 * MAX_WAIT);
    //Make sure things are good:
    Assert.IsTrue(deltas.Count == TESTS, "Everybody ran");
    foreach(double err in deltas) {
     Assert.IsTrue( err < 2.0, String.Format("Latency too long: {0}",err)); 
    }
  }

}
#endif

}
