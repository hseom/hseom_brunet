using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;

using Brunet;
using Brunet.Transport;
using Brunet.Messaging;
using Brunet.Connections;
using Brunet.Services.MapReduce;
using Brunet.Symphony;
using Brunet.Concurrent;
using Brunet.Util;

namespace Brunet.Services.Coordinate
{
	public class DeetooCluster {
		protected Node _cnode;
		protected Node _qnode;
		protected double _coor_x;
		protected double _coor_y;
		//protected object _sync;

		public DeetooCluster(Node cnode, Node qnode, double x, double y) {
			//_sync = new object();
			_cnode = cnode;
			_qnode = qnode;
			_coor_x = x;
			_coor_y = y;
		}

		public string[] getRange(int net_size, double alpha) {
			Console.WriteLine("[CLUSTER] getRange function called!!");
			double pow = Math.Pow(2, 160);
			Console.WriteLine("[CLUSTER] pow : {0}", pow);
			double sq = (double)((Math.Sqrt( alpha / (double)net_size )));
			Console.WriteLine("[CLUSTER] sq: {0}", sq);
			double range = sq * pow;
			Console.WriteLine("[CLUSTER] range: {0}", range);
			double start = 1;
			string[] rg = new string[2];
			Random random = new Random();
			while( start % 2 != 0) {
				int shift = random.Next(0, 160); 
				start = 1 << shift;
				Console.WriteLine("random number : {0}", start);
			}
			double end = start + range;

			string start_addr = start.ToString();
			Console.WriteLine("start_address : {0}", start_addr);
			rg[0] = start_addr;
			string end_addr = end.ToString();
			Console.WriteLine("end_address : {0}", end_addr);
			rg[1] = end_addr;

			return rg;
		}

		public void Start() {
			Channel q_channel = new Channel(1);
			q_channel.CloseEvent += QueryHandler;
			Hashtable query_arg = new Hashtable();
			ArrayList map_arg = new ArrayList();
			ArrayList gen_arg = new ArrayList();
			ArrayList reduce_arg = new ArrayList();
			query_arg.Add("task_name", "Brunet.Services.Deetoo.MapReduceQuery");
			/*
			int net_size = _cnode.NetworkSize;
			double alpha = 5.0;
			string[] range = getRange(net_size, alpha);
			string st = range[0] as string;
			string ed = range[1] as string;
			gen_arg.Add(st);
			gen_arg.Add(ed);
			*/
			gen_arg.Add("brunet:node:3VMHZHKYYYYP62EEACCCK4J74SKR7CJ4");
			gen_arg.Add("brunet:node:XWQCFO6CDVVSLGASDVLYXQHZRDHCFHSU");
			map_arg.Add("cluster");
			Dictionary<string, double> my_coor= new Dictionary<string, double>();
			my_coor["x"] = _coor_x;
			my_coor["y"] = _coor_y;
			my_coor["radius"] = 7000.0;
			map_arg.Add(my_coor);
			reduce_arg.Add("maxcount");
			int max_count = 10;
			reduce_arg.Add(max_count);

			query_arg.Add("gen_arg", gen_arg);
			query_arg.Add("map_arg", map_arg);
			query_arg.Add("reduce_arg", reduce_arg);
			try {
				_qnode.Rpc.Invoke(_qnode, q_channel, "mapreduce.Start", query_arg);
			}
			catch(Exception) {}
		}

		protected void QueryHandler(object o, EventArgs args) {
			Channel dc_q = (Channel) o;
			RpcResult result = dc_q.Dequeue() as RpcResult;
			IDictionary idic = null;
			try {
				idic = (IDictionary) result.Result;
			}
			catch(Exception) {}
			if (idic != null) {
				Console.WriteLine("[CLUSTER] query result returned");
			}
			else {
				Console.WriteLine("[CLUSTER] no query result returned");
			}
			ArrayList query_result = null;
			query_result = idic["query_result"] as ArrayList;
			Console.WriteLine("[CLUSTER] The number of returned results : {0}", query_result.Count);
			if (query_result.Count <= 0) {
				Console.WriteLine("[CLUSTER] Do cache action");
				CacheAction();	
			}
			else {
				Console.WriteLine("[CLUSTER] Do cluster join action");
			}
		}

		protected void CacheAction() {
			double alpha = 5.0;
			Channel c_channel = new Channel(1);
			c_channel.CloseEvent += CacheHandler;
			Hashtable cache_arg = new Hashtable();
			ArrayList map_arg = new ArrayList();
			ArrayList gen_arg = new ArrayList();
			cache_arg.Add("task_name", "Brunet.Services.Deetoo.MapReduceCache");
			gen_arg.Add("brunet:node:DVLYXQHZRDHCFHSUXWQCFO6CDVVSLGAS");
			gen_arg.Add("brunet:node:QMFO77B2VGSBXEDEWAA4EUXYNMTXAZPM");
			Dictionary<string, double> my_coor= new Dictionary<string, double>();
			my_coor["x"] = _coor_x;
			my_coor["y"] = _coor_y;
			map_arg.Add(my_coor);
			map_arg.Add(alpha);
			map_arg.Add("brunet:node:DVLYXQHZRDHCFHSUXWQCFO6CDVVSLGAS");
			map_arg.Add("brunet:node:QMFO77B2VGSBXEDEWAA4EUXYNMTXAZPM");
			cache_arg.Add("gen_arg", gen_arg);
			cache_arg.Add("map_arg", map_arg);
			try {
				_cnode.Rpc.Invoke(_cnode, c_channel, "mapreduce.Start", cache_arg);
			}catch(Exception) {}
		}

		protected void CacheHandler(object o, EventArgs args) {
			Channel dc_c = (Channel) o;
			RpcResult result = dc_c.Dequeue() as RpcResult;
			IDictionary idic = null;
			try {
				idic = (IDictionary)result.Result;
			}catch(Exception) {}
			if (idic != null) {
				Console.WriteLine("[CLUSTER] cache result returned");
			}
			else {
				Console.WriteLine("[CLUSTER] no cache result returned");
			}
			int success;
			success = (int) idic["success"];
			Console.WriteLine("[CLUSTER] The number of cache successes : {0}", success);
		}
	}
}	
