/*
 * Created by SharpDevelop.
 * User: Timothy
 * Date: 4/05/2016
 * Time: 12:05
 * 
 * To change this template use Tools | Options | Coding | Edit Standard Headers.
 */
using System;
using System.Collections.Generic;
using System.Management;
using System.Threading;
using System.IO;
using System.Xml.Serialization;
using System.Xml;
using System.Net;
using System.Text;
using System.Collections.Specialized;

namespace VeeamProcess
{
	[XmlRootAttribute("UpdatePost")]
	public class UpdatePost
	{
		[XmlArray("VeeamProcesses")]
		[XmlArrayItem("VeeamProcess")]
		public List<VeeamProcess> veeamProcesses;
		[XmlElement("Server")]
		public string server;
		[XmlElement("Date")]
		public int date;

		
		public 	UpdatePost() {
			
		}
		public 	UpdatePost(List<VeeamProcess> p,string server) {
			this.veeamProcesses = p;
			this.server = server;
			TimeSpan t = DateTime.UtcNow - new DateTime(1970, 1, 1);
			this.date = (int)t.TotalSeconds;
			
		}
	}
	
	public class VeeamProcess 
	{
		[XmlElement("ProcessName")]
		public String process;
		[XmlElement("CommandLine")]
		public String commandLine;
		[XmlElement("ExecutablePath")]
		public String execPath;
		[XmlElement("ProcessID")]
		public uint processid;
		[XmlElement("ParentProcessID")]
		public uint parentprocessid;
		[XmlElement("Stats")]
		public VeeamProcessStat stats;
		
		public VeeamProcess() {}
		public VeeamProcess(String process, String commandLine,String execPath,uint processid,uint parentprocessid) {
			this.process = process;
			this.commandLine = commandLine;
			this.execPath = execPath;
			this.processid = processid;
			this.parentprocessid = parentprocessid;
		}

	}
	public class VeeamProcessStat
	{
		[XmlElement("IOBytesPerSec")]
		public ulong IOBytesPerSec;
		public ulong WorkingSetPrivate;
		public float CpuPct;
		
		public VeeamProcessStat() {}
		public VeeamProcessStat(ulong IOBytesPerSec,ulong WorkingSetPrivate,float CpuPct) {
			this.IOBytesPerSec = IOBytesPerSec;
			this.WorkingSetPrivate = WorkingSetPrivate;
			this.CpuPct = CpuPct;
		}
	}
	class VeeamProccessControler {
		private ManagementObjectSearcher commandLineSearcher;
		private ManagementObjectSearcher procStatQ;
		private float cores;
		
		public VeeamProccessControler() {
			this.commandLineSearcher = new ManagementObjectSearcher("SELECT Name,CommandLine,ExecutablePath,ProcessId,ParentProcessId FROM Win32_Process WHERE NAME Like '[Vv]eeam%'");
			this.procStatQ = new ManagementObjectSearcher("SELECT * FROM Win32_PerfFormattedData_PerfProc_Process WHERE NAME Like '[Vv]eeam%'");
			
			
			//initial set so we don't get empty val
			this.commandLineSearcher.Get();
			this.procStatQ.Get();
			
			var cs = new ManagementObjectSearcher("select NumberOfLogicalProcessors from win32_processor");
			cs.Get();
			foreach (ManagementObject mo in cs.Get()) {
				this.cores = (float)((uint)mo["NumberOfLogicalProcessors"]);
			}
			Console.WriteLine(cores);
					
		}
					
		public List<VeeamProcess> get() {
			var vprocs = new List<VeeamProcess>();
			var stats = new Dictionary<uint, VeeamProcessStat>();
			foreach (ManagementObject mo in procStatQ.Get()) {
				var cpupct = ((float)(ulong)mo["PercentProcessorTime"])/this.cores;
				stats.Add((uint)mo["IDProcess"],new VeeamProcessStat((ulong)mo["IOOtherBytesPerSec"],
				                                                     (ulong)mo["WorkingSetPrivate"],
				                                                     cpupct
				                                                    ));
			}
			
			foreach (ManagementObject mo  in commandLineSearcher.Get()) {
				var procid = (uint)mo["ProcessId"];
				var newProc = new VeeamProcess((string)mo["Name"],(string)mo["CommandLine"],(string)mo["ExecutablePath"],procid,(uint)mo["ParentProcessId"]);
				if (stats.ContainsKey(procid)) {
					newProc.stats = stats[procid];
				}
				
				
				vprocs.Add(newProc);
			}
			return vprocs;
		}
			
	}
	public class Utf8StringWriter : StringWriter
	{
	    // Use UTF8 encoding but write no BOM to the wire
	    public override Encoding Encoding
	    {
	         get { return new UTF8Encoding(false); } 
	    }
	}
	class Program
	{
		public static void Main(string[] args)
		{
			if (args.Length > 1) {
				var ctr =  new VeeamProccessControler();
				
				string server = args[0];
				string key = args[1];
				int port = 46101;
				if (args.Length > 2) {
					port = Int32.Parse(args[2]);
				}
				
				string url = String.Format("http://{0}:{1}/postproc",server,port);
				
				bool stopagent = false;
				
				string hostname = Dns.GetHostName();

				
				while (!stopagent) {
					Console.WriteLine("Start collecting..");
					var x = new System.Xml.Serialization.XmlSerializer(typeof(UpdatePost));
					
					var writer = new Utf8StringWriter();
					x.Serialize(writer,new UpdatePost(ctr.get(),hostname));
					
					Console.WriteLine("Uploading..");
					var wb = new WebClient();
					var data = new NameValueCollection();
    				data["key"] = key;
    				data["update"] = writer.ToString();

    				try {
				    	var response = wb.UploadValues(url, "POST", data);
				    	var responseString = System.Text.Encoding.UTF8.GetString(response);
				    	
				    	if (responseString == "Stopnow") {
				    		Console.WriteLine("Stopping");
				    		stopagent = true;
				    	} else {
				    		Console.WriteLine("Succesful : "+responseString);
				    	}
    				} catch (Exception e) {
    					Console.WriteLine("Failure to upload "+e.Message);
    					Console.WriteLine("Data lost : "+data["update"]);
    				}
    				if (!stopagent) {
						System.Threading.Thread.Sleep(10000);
    				}
					
				}
			} else {
				Console.WriteLine("<exe> <server> <key> <port>");
			}
			
		}
	}
}