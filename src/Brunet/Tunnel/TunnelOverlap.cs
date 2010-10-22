/*
This program is part of BruNet, a library for the creation of efficient overlay
networks.
Copyright (C) 2009 David Wolinsky <davidiw@ufl.edu>, University of Florida

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
using System.Collections;
using System.Collections.Generic;
using System.Threading;

#if BRUNET_NUNIT
using NUnit.Framework;
using System.Security.Cryptography;
#endif

using Brunet.Connections;
using Brunet.Transport;
using Brunet.Util;

using Brunet.Symphony;
namespace Brunet.Tunnel {
  public interface ITunnelOverlap {
    /// <summary>Selects an Address from the msg which should be a good
    /// candidate for connecting to as a proxy.</summary>
    Address EvaluatePotentialOverlap(IDictionary msg);
    /// <summary>Returns a list of addresses that contain an overlap between
    /// the connection list and the sync message.  This can be filtered for
    /// performance / fault tolerance purpose.</summary>
    List<Connection> EvaluateOverlap(ConnectionList con, IDictionary msg);
    /// <summary>Returns a Tunnel Sync message containing information to
    /// be used to determine overlap.</summary>
    IDictionary GetSyncMessage(IList<Connection> current_overlap,
        Address local_addr, ConnectionList cons);
    /// <summary>Attempt to FindOverlap based upon our connections and the
    /// Remote TunnelTA.</summary>
    List<Connection> FindOverlap(TunnelTransportAddress ta, ConnectionList cons);
  }

  public class SimpleTunnelOverlap : ITunnelOverlap {
    /// <summary>Returns the 4 oldest connections.</summary>
    protected List<Connection> GetOldest(ConnectionList cons)
    {
      List<Connection> lcons = new List<Connection>(cons.Count);
      foreach(Connection con in cons) {
        lcons.Add(con);
      }

      return GetOldest(lcons);
    }

    protected List<Connection> GetOldest(List<Connection> cons)
    {
      List<Connection> oldest = new List<Connection>(cons);
      Comparison<Connection> comparer = delegate(Connection x, Connection y) {
        return x.CreationTime.CompareTo(y.CreationTime);
      };

      oldest.Sort(comparer);
      if(oldest.Count > 4) {
        oldest.RemoveRange(0, oldest.Count - 4);
      }
      return oldest;
    }

    /// <summary>Always returns the oldest non-tunnel address.</summary>
    public virtual Address EvaluatePotentialOverlap(IDictionary msg)
    {
      Address oldest_addr = null;
      int oldest_age = -1;
      foreach(DictionaryEntry de in msg) {
        MemBlock key = de.Key as MemBlock;
        if(key == null) {
          key = MemBlock.Reference((byte[]) de.Key);
        }
        Address addr = new AHAddress(key);

        Hashtable values = de.Value as Hashtable;
        TransportAddress.TAType tatype =
          TransportAddressFactory.StringToType(values["ta"] as string);

        if(tatype.Equals(TransportAddress.TAType.Tunnel)) {
          continue;
        }

        int age = (int) values["ct"];
        if(age > oldest_age) {
          oldest_addr = addr;
        }
      }

      return oldest_addr;
    }

    /// <summary>Returns the oldest 4 addresses in the overlap.</summary>
    public virtual List<Connection> EvaluateOverlap(ConnectionList cons, IDictionary msg)
    {
      List<Connection> overlap = new List<Connection>();

      foreach(DictionaryEntry de in msg) {
        MemBlock key = de.Key as MemBlock;
        if(key == null) {
          key = MemBlock.Reference((byte[]) de.Key);
        }
        Address addr = new AHAddress(key);

        int index = cons.IndexOf(addr);
        if(index < 0) {
          continue;
        }

        Connection con = cons[index];

        // Since there are no guarantees about routing over two tunnels, we do
        // not consider cases where we are connected to the overlapping tunnels
        // peers via tunnels
        if(con.Edge.TAType.Equals(TransportAddress.TAType.Tunnel)) {
          Hashtable values = de.Value as Hashtable;
          TransportAddress.TAType tatype =
            TransportAddressFactory.StringToType(values["ta"] as string);
          if(tatype.Equals(TransportAddress.TAType.Tunnel)) {
            continue;
          }
        }
        overlap.Add(con);
      }

      return GetOldest(overlap);
    }

    /// <summary>Returns a Tunnel Sync message containing up to 40 addresses
    /// first starting with previous overlap followed by new potential
    /// connections for overlap.</summary>
    public virtual IDictionary GetSyncMessage(IList<Connection> current_overlap,
        Address local_addr, ConnectionList cons)
    {
      Hashtable ht = new Hashtable();
      DateTime now = DateTime.UtcNow;
      if(current_overlap != null) {
        foreach(Connection con in current_overlap) {
          Hashtable info = new Hashtable(2);
          info["ta"] = TransportAddress.TATypeToString(con.Edge.TAType);
          info["ct"] = (int) (now - con.CreationTime).TotalMilliseconds;
          ht[con.Address.ToMemBlock()] = info;
        }
      }

      int idx = cons.IndexOf(local_addr);
      if(idx < 0) {
        idx = ~idx;
      }
      int max = cons.Count < 16 ? cons.Count : 16;
      int start = idx - max / 2;
      int end = idx + max / 2;

      for(int i = start; i < end; i++) {
        Connection con = cons[i];
        MemBlock key = con.Address.ToMemBlock();
        if(ht.Contains(key)) {
          continue;
        }
        Hashtable info = new Hashtable();
        info["ta"] = TransportAddress.TATypeToString(con.Edge.TAType);
        info["ct"] = (int) (now - con.CreationTime).TotalMilliseconds;
        ht[key] = info;
      }

      return ht;
    }

    /// <summary>Attempt to find the overlap in a remote TunnelTransportAddress
    /// and our local node.  This will be used to help communicate with a new
    /// tunneled peer.</summary>
    public virtual List<Connection> FindOverlap(TunnelTransportAddress ta, ConnectionList cons)
    {
      List<Connection> overlap = new List<Connection>();
      foreach(Connection con in cons) {
        if(ta.ContainsForwarder(con.Address)) {
          overlap.Add(con);
        }
      }

      return GetOldest(overlap);
    }
  }
#if BRUNET_NUNIT
  [TestFixture]
  public class SimpleTunnelOverlapTester {
    [Test]
    public void Test()
    {
      ITunnelOverlap _ito = new SimpleTunnelOverlap();
      Address addr_x = new AHAddress(new RNGCryptoServiceProvider());
      byte[] addrbuff = Address.ConvertToAddressBuffer(addr_x.ToBigInteger() + (Address.Full / 2));
      Address.SetClass(addrbuff, AHAddress._class);
      Address addr_y = new AHAddress(addrbuff);

      ArrayList addresses = new ArrayList();
      ConnectionTable ct_x = new ConnectionTable();
      ConnectionTable ct_y = new ConnectionTable();
      ConnectionTable ct_empty = new ConnectionTable();
      for(int i = 1; i <= 10; i++) {
        addrbuff = Address.ConvertToAddressBuffer(addr_x.ToBigInteger() + (i * Address.Full / 16));
        Address.SetClass(addrbuff, AHAddress._class);
        addresses.Add(new AHAddress(addrbuff));

        TransportAddress ta = TransportAddressFactory.CreateInstance("brunet.tcp://158.7.0.1:5000");
        Edge fe = new FakeEdge(ta, ta, TransportAddress.TAType.Tcp);
        ct_x.Add(new Connection(fe, addresses[i - 1] as AHAddress, "structured", null, null));
        if(i == 10) {
          ct_y.Add(new Connection(fe, addresses[i - 1] as AHAddress, "structured", null, null));
        }
      }


      ConnectionType con_type = ConnectionType.Structured;
      IDictionary id = _ito.GetSyncMessage(null, addr_x, ct_x.GetConnections(con_type));
      Assert.AreEqual(_ito.EvaluateOverlap(ct_y.GetConnections(con_type), id)[0].Address, addresses[9], "Have an overlap!");
      Assert.AreEqual(_ito.EvaluateOverlap(ct_empty.GetConnections(con_type), id).Count, 0, "No overlap!");
      Assert.AreEqual(addresses.Contains(_ito.EvaluatePotentialOverlap(id)), true, "EvaluatePotentialOverlap returns valid!");
    }
  }
#endif
}
