/*
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
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using Brunet.Connections;
using Brunet.Services.Coordinate;
using Brunet.Symphony;
using Brunet.Tunnel;
using Brunet.Transport;
using Brunet.Util;

namespace Brunet.Simulator {
  public class TunnelOverlapSimulator : Simulator {
    public static void Main(string []args)
    {
      TunnelOverlapSimulator sim = new TunnelOverlapSimulator();
      bool complete;
      Runner.ParseCommandLine(args, out complete, sim);
      sim.Complete();

      Address addr1 = null, addr2 = null;
      sim.AddDisconnectedPair(out addr1, out addr2, sim.NCEnable);
      sim.Complete();

      SimpleTimer.RunSteps(1000000, false);
      StructuredNode node1 = (sim.Nodes[addr1] as NodeMapping).Node as StructuredNode;
      StructuredNode node2 = (sim.Nodes[addr2] as NodeMapping).Node as StructuredNode;
      node1.ManagedCO.AddAddress(addr2);
      SimpleTimer.RunSteps(100000, false);

      Console.WriteLine(addr1 + "<=>" + addr2 + ":");
      Console.WriteLine("\t" + node1.ConnectionTable.GetConnection(ConnectionType.Structured, addr2) + "\n");
      sim.PrintConnections(node1);
      Console.WriteLine();
      sim.PrintConnections(node2);

      Console.WriteLine("\nPhase 2 -- Disconnect...");
      Hashtable ht = new Hashtable();
      foreach(Connection con in node1.ConnectionTable.GetConnections(Tunnel.OverlapConnectionOverlord.STRUC_OVERLAP)) {
        ht[con.Address] = (con.Edge as SimulationEdge).Delay;
        Console.WriteLine("Closing: " + con);
        con.Edge.Close();
      }


      ConnectionList cl2 = node2.ConnectionTable.GetConnections(ConnectionType.Structured);
      foreach(DictionaryEntry de in ht) {
        Address addr = de.Key as Address;
        int delay = (int) de.Value;
        int index = cl2.IndexOf(addr);
        if(index < 0) {
          Console.WriteLine("No matching pair for overlap...");
          continue;
        }
        Connection con = cl2[index];
        delay += (con.Edge as SimulationEdge).Delay;
        Console.WriteLine("Delay: " + delay);
      }
      ht.Clear();

      foreach(Connection con in node2.ConnectionTable.GetConnections(Tunnel.OverlapConnectionOverlord.STRUC_OVERLAP)) {
        ht[con.Address] = (con.Edge as SimulationEdge).Delay;
        Console.WriteLine("Closing: " + con);
        con.Edge.Close();
      }

      ConnectionList cl1 = node1.ConnectionTable.GetConnections(ConnectionType.Structured);
      foreach(DictionaryEntry de in ht) {
        Address addr = de.Key as Address;
        int delay = (int) de.Value;
        int index = cl1.IndexOf(addr);
        if(index < 0) {
          Console.WriteLine("No matching pair for overlap...");
          continue;
        }
        Connection con = cl1[index];
        delay += (con.Edge as SimulationEdge).Delay;
        Console.WriteLine("Delay: " + delay);
      }

      SimpleTimer.RunSteps(100000, false);

      Console.WriteLine(addr1 + "<=>" + addr2 + ":");
      Console.WriteLine("\t" + node1.ConnectionTable.GetConnection(ConnectionType.Structured, addr2) + "\n");
      sim.PrintConnections(node1);
      Console.WriteLine();
      sim.PrintConnections(node2);

      sim.Disconnect();
    }

    // adds a disconnected pair to the pool
    public void AddDisconnectedPair(out Address address1, out Address address2, bool nctunnel)
    {
      address1 = new AHAddress(new RNGCryptoServiceProvider());
      byte[] addrbuff = Address.ConvertToAddressBuffer(address1.ToBigInteger() + (Address.Full / 2));
      Address.SetClass(addrbuff, AHAddress._class);
      address2 = new AHAddress(addrbuff);

      AddDisconnectedPair(address1, address2, nctunnel);
    }

    public void AddDisconnectedPair(Address address1, Address address2, bool nctunnel)
    {
      NodeMapping nm1 = new NodeMapping();
      nm1.ID = TakeID();
      TakenIDs[nm1.ID] = nm1.ID;
      NodeMapping nm2 = new NodeMapping();
      nm2.ID = TakeID();
      TakenIDs[nm2.ID] = nm2.ID;

      AddBrokenNode(ref nm1, address1, nm2.ID, nctunnel);
      Nodes[address1] = nm1;

      AddBrokenNode(ref nm2, address2, nm1.ID, nctunnel);
      Nodes[address2] = nm2;
    }

    protected void AddBrokenNode(ref NodeMapping nm, Address addr, int broken_port, bool nctunnel)
    {
      nm.Node = new StructuredNode(addr as AHAddress, BrunetNamespace);

      TAAuthorizer auth = new IDTAAuthorizer(broken_port);
      nm.Node.AddEdgeListener(new SimulationEdgeListener(nm.ID, 0, auth, true));

      ITunnelOverlap ito = null;
      if(NCEnable) {
        nm.NCService = new NCService(nm.Node, new Point());
// Until we figure out what's going on with VivaldiTargetSelector its not quite useful for these purposes
//        (nm.Node as StructuredNode).Sco.TargetSelector = new VivaldiTargetSelector(nm.Node, ncservice);
      }
      if(nctunnel && NCEnable) {
        ito = new NCTunnelOverlap(nm.NCService);
      } else {
        ito = new SimpleTunnelOverlap();
      }

      nm.Node.AddEdgeListener(new Tunnel.TunnelEdgeListener(nm.Node, ito));

      ArrayList RemoteTAs = new ArrayList();
      for(int i = 0; i < 5 && i < TakenIDs.Count; i++) {
        int rport = (int) TakenIDs.GetByIndex(_rand.Next(0, TakenIDs.Count));
        RemoteTAs.Add(TransportAddressFactory.CreateInstance("brunet.function://127.0.0.1:" + rport));
      }
      nm.Node.RemoteTAs = RemoteTAs;

      nm.Node.Connect();
    }
  }
}
