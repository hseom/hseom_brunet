using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text.RegularExpressions;

namespace Brunet.Services.Deetoo {

  public abstract class QueryMatcher {
    /** returns true if this object matches the current query
     */
    public abstract bool Match(object data);
  }

  public class RegexMatcher : QueryMatcher {
    private readonly Regex RE;
    public RegexMatcher(string pattern) {
      RE = new Regex(pattern);
    }
    public override bool Match(object data) {
      return RE.IsMatch(data.ToString());
    }
  }
  public class ExactMatcher : QueryMatcher {
    private readonly object Value;
    public ExactMatcher(object val) {
      Value = val;
    }
    public override bool Match(object data) {
      return Value != null ? Value.Equals(data) : data == null;
    }
  }
  public class ProxMatcher : QueryMatcher {
    private readonly double X;
    private readonly double Y;
    private readonly double Radius;
    public ProxMatcher(object coor) {
      IDictionary my_coor = (IDictionary) coor;
      X = (double)my_coor["x"];
      Y = (double)my_coor["y"];
      Radius = (double)my_coor["radius"];
    }
    public override bool Match(object data) {
      double proximity;
      IDictionary coordinate = (IDictionary) data;
      if (coordinate == null) {
        return false;
      }
      proximity = Math.Sqrt(Math.Pow((double)coordinate["x"] - X, 2) + Math.Pow((double)coordinate["y"] - Y, 2));
      if (proximity < Radius) {
        return true;
      }else {
        return false;
      }
    }
  }
}
