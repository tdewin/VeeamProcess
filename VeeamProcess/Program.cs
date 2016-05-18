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
using System.Net.NetworkInformation;

namespace VeeamProcess
{
	/*
	 * Answer deserialization, should become std way of getting feedback 
	 * 
	 * 
	 */
	[Serializable()]
	public class Answer
	{
	    [System.Xml.Serialization.XmlElement("Error")]
	    public string Error { get; set; }
	
	    [System.Xml.Serialization.XmlElement("Success")]
	    public string Success { get; set; }
	
	    [System.Xml.Serialization.XmlElement("Cn")]
	    public ulong Cn { get; set; }
	}
	
	/*
	 * Construction of the updatepost, by serializing, you will get perfect output 
	 */
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
		[XmlElement("Cn")]
		public ulong cn;
		[XmlElement("Stats")]
		public VeeamServerStat stats;
		
		public 	UpdatePost() {
			
		}
		public 	UpdatePost(List<VeeamProcess> p,VeeamServerStat stats, string server,ulong cn) {
			this.veeamProcesses = p;
			this.server = server;
			this.stats = stats;
			TimeSpan t = DateTime.UtcNow - new DateTime(1970, 1, 1);
			this.date = (int)t.TotalSeconds;
			this.cn = cn;
		}
	}
	public class VeeamServerStat
	{
		[XmlElement("NetBytesPerSec")]
		public ulong NetBytesPerSec;
		[XmlElement("DiskBytesPerSec")]
		public ulong DiskBytesPerSec;
		[XmlElement("DiskTransfersPerSec")]
		public ulong DiskTransfersPerSec;
		[XmlElement("Cores")]
		public uint Cores;
		
		public VeeamServerStat() {}
		public VeeamServerStat(ulong NetBytesPerSec,ulong DiskBytesPerSec,ulong DiskTransfersPerSec,uint cores) {
			this.NetBytesPerSec = NetBytesPerSec;
			this.DiskBytesPerSec = DiskBytesPerSec;
			this.DiskTransfersPerSec = DiskTransfersPerSec;
			this.Cores = cores;
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
		[XmlElement("WorkingSetPrivate")]
		public ulong WorkingSetPrivate;
		[XmlElement("CpuPct")]
		public float CpuPct;
		
		public VeeamProcessStat() {}
		public VeeamProcessStat(ulong IOBytesPerSec,ulong WorkingSetPrivate,float CpuPct) {
			this.IOBytesPerSec = IOBytesPerSec;
			this.WorkingSetPrivate = WorkingSetPrivate;
			this.CpuPct = CpuPct;
		}
		
	}
	
	
	/*
	 * Classes for intermediate storing of results between runs
	 * This insure more avg values between updates as we use raw classes instead of precooked data.
	 */
	public class RawVeeamProcessStat 
	{
		public ulong TimeStamp { get; set; }
		public ulong Frequency { get; set; }
		public ulong Timestamp_Sys100NS {get;set;}
		public ulong IOBytesPerSec { get; set; }
		public ulong WorkingSetPrivate { get; set; }
		public float CpuPct { get; set; }
		
		
		public RawVeeamProcessStat(ulong timeStamp, ulong frequency, ulong Timestamp_Sys100NS, ulong iOBytesPerSec, ulong workingSetPrivate, float cpuPct)
		{
			this.TimeStamp = timeStamp;
			this.Frequency = frequency;
			this.Timestamp_Sys100NS = Timestamp_Sys100NS;
			
			this.IOBytesPerSec = iOBytesPerSec;
			this.WorkingSetPrivate = workingSetPrivate;
			this.CpuPct = cpuPct;
			
		}
		public RawVeeamProcessStat()
		{
			
		}
	}

	public class RawVeeamServerStatFreqCounter 
	{
		public ulong TimeStamp { get; set; }
		public ulong Frequency { get; set; }
		public ulong Counter { get; set; }

		
		public RawVeeamServerStatFreqCounter(ulong timeStamp, ulong frequency, ulong counterpersec)
		{
			this.TimeStamp = timeStamp;
			this.Frequency = frequency;
			this.Counter = counterpersec;
		}
		public RawVeeamServerStatFreqCounter()
		{
			
		}
		public void addUnit(ulong add) {
			this.Counter += add;
		}
	}	
	
	
	/*
	 * Controller that generates performance stats 
	 * In a seperate class so logic might (maybe not use slow WMI queries in the future)
	 * 
	 */
	public class VeeamProccessControler {
		private ManagementObjectSearcher commandLineSearcher;
		private ManagementObjectSearcher procStatQ;
		private ManagementObjectSearcher networkQ;
		private ManagementObjectSearcher diskQ;

		private Dictionary<uint,RawVeeamProcessStat> proccache;
		
		private RawVeeamServerStatFreqCounter net;
		private RawVeeamServerStatFreqCounter disk;
		private RawVeeamServerStatFreqCounter diskIo;


	
		
		public uint cores {get;set;}
		
		public VeeamProccessControler() {
			this.commandLineSearcher = new ManagementObjectSearcher("SELECT Name,CommandLine,ExecutablePath,ProcessId,ParentProcessId FROM Win32_Process WHERE NAME Like '[Vv]eeam%'");
			this.procStatQ = new ManagementObjectSearcher("SELECT * FROM Win32_PerfRawData_PerfProc_Process WHERE NAME Like '[Vv]eeam%'");
			this.networkQ = new ManagementObjectSearcher("SELECT BytesTotalPersec,Frequency_PerfTime,Timestamp_PerfTime  FROM Win32_PerfRawData_Tcpip_NetworkInterface");
			this.diskQ = new ManagementObjectSearcher("SELECT DiskBytesPersec,Frequency_PerfTime,Timestamp_PerfTime,DiskTransfersPerSec FROM Win32_PerfRawData_PerfDisk_PhysicalDisk WHERE NAME Like '_Total'");
	
			
			var cs = new ManagementObjectSearcher("select NumberOfLogicalProcessors from win32_processor");
			cs.Get();
			foreach (ManagementObject mo in cs.Get()) {
				this.cores =(uint)mo["NumberOfLogicalProcessors"];
			}
			this.proccache = new Dictionary<uint, RawVeeamProcessStat>();
			
			this.getServerUsage();
			this.getProc();

					
		}

		public VeeamServerStat getServerUsage() {
			RawVeeamServerStatFreqCounter netstat = null;
			foreach (ManagementObject mo in this.networkQ.Get()) {
				if (netstat == null) {
					netstat = new RawVeeamServerStatFreqCounter((ulong)mo["Timestamp_PerfTime"],(ulong)mo["Frequency_PerfTime"],(ulong)mo["BytesTotalPersec"]);
				} else {
					netstat.addUnit((ulong)mo["BytesTotalPersec"]);
				}
				
			}
			RawVeeamServerStatFreqCounter diskstat = null;
			RawVeeamServerStatFreqCounter diskstatio = null;
			foreach (ManagementObject mo in this.diskQ.Get()) {
				if (diskstat == null) {
					diskstat = new RawVeeamServerStatFreqCounter((ulong)mo["Timestamp_PerfTime"],(ulong)mo["Frequency_PerfTime"],(ulong)mo["DiskBytesPersec"]);
				} else {
					diskstat.addUnit((ulong)mo["DiskBytesPersec"]);
				}
				
				if (diskstatio == null) {
					diskstatio = new RawVeeamServerStatFreqCounter((ulong)mo["Timestamp_PerfTime"],(ulong)mo["Frequency_PerfTime"],(ulong)((uint)mo["DiskTransfersPerSec"]));
				} else {
					diskstatio.addUnit((ulong)((ulong)mo["DiskTransfersPerSec"]));
				}
				
			}			
						
			var stat = new VeeamServerStat(0,0,0,this.cores);
			
			if (this.net != null) {
				stat.NetBytesPerSec = (ulong)((double)(netstat.Counter-this.net.Counter)/(((double)(netstat.TimeStamp-this.net.TimeStamp))/netstat.Frequency));
			} 
			this.net = netstat;
			
			if (this.disk != null) {
				stat.DiskBytesPerSec = (ulong)((double)(diskstat.Counter-this.disk.Counter)/(((double)(diskstat.TimeStamp-this.disk.TimeStamp))/netstat.Frequency));
			} 
			this.disk = diskstat;
			
			
			if (this.diskIo != null) {
				stat.DiskTransfersPerSec = (ulong)((double)(diskstatio.Counter-this.diskIo.Counter)/(((double)(diskstatio.TimeStamp-this.diskIo.TimeStamp))/netstat.Frequency));
			} 
			this.diskIo = diskstatio;	
			
			return stat;
		}
		public List<VeeamProcess> getProc() {
			var vprocs = new List<VeeamProcess>();
			var stats = new Dictionary<uint, VeeamProcessStat>();
			
			
			foreach (ManagementObject mo in procStatQ.Get()) {
				var cpupct = ((float)(ulong)mo["PercentProcessorTime"]);
				var io = (ulong)mo["IODataBytesPersec"];
				var mem = (ulong)mo["WorkingSetPrivate"];
				var time = (ulong)mo["Timestamp_PerfTime"];
				var freq = (ulong)mo["Frequency_PerfTime"];
				var sys100ns = (ulong)mo["Timestamp_Sys100NS"];
				var pid = (uint)mo["IDProcess"];
				
				var stat = new RawVeeamProcessStat(time,freq,sys100ns,io,mem,cpupct);
				
				if ( this.proccache.ContainsKey(pid) ) {
					var old = this.proccache[pid];
					
					
					double tio = ((double)(stat.TimeStamp-old.TimeStamp))/stat.Frequency;
					//System.Console.WriteLine(tio);
					ulong realio =  (ulong)((double)(stat.IOBytesPerSec-old.IOBytesPerSec)/tio);
					ulong realmem = (ulong)(stat.WorkingSetPrivate);
					float realcpu = (float)((double)(stat.CpuPct-old.CpuPct)*100/((stat.Timestamp_Sys100NS-old.Timestamp_Sys100NS)));
					
					stats[pid] = new VeeamProcessStat(realio,realmem,realcpu);
					
				}
				this.proccache[pid] = stat;
				
			}
			foreach (ManagementObject mo  in commandLineSearcher.Get()) {
				var procid = (uint)mo["ProcessId"];
				var newProc = new VeeamProcess((string)mo["Name"],(string)mo["CommandLine"],(string)mo["ExecutablePath"],procid,(uint)mo["ParentProcessId"]);
				if (stats.ContainsKey(procid)) {
					newProc.stats = stats[procid];
				} else {
					newProc.stats = new VeeamProcessStat(0,0,0);
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
	

	/*
	 * Main program, use the collector to get the data and then post via http in a loop
	 * 
	 */
	class Program
	{
		//http://stackoverflow.com/questions/804700/how-to-find-fqdn-of-local-machine-in-c-net
		public static string GetFQDN()
		{
		    string domainName = IPGlobalProperties.GetIPGlobalProperties().DomainName;
		    string hostName = Dns.GetHostName();
		
		    string dotDomainName = "." + domainName;
		    if(domainName != "" && !hostName.EndsWith(dotDomainName))  // if hostname does not already include domain name
		    {
		        hostName += dotDomainName;   // add the domain name part
		    }
		
		    return hostName;                    // return the fully qualified name
		}
		
		public static Answer DeserializeAnswer(byte[] response) {
			Answer a = new Answer();
			a.Error = "Unmarshall Error";
	        
			try {
				MemoryStream stream = new MemoryStream(response);
				StreamReader reader = new StreamReader(stream);
				
				var xa = new XmlSerializer(typeof(Answer));
				a = (Answer)xa.Deserialize(reader);
			} catch (Exception e) {
				a.Error += e.Message;
			}
			return a;
		}
		
		public static void Main(string[] args)
		{
			if (args.Length > 0) {
				var ctr =  new VeeamProccessControler();
				
				string server = "127.0.0.1";
				string key = "";
				int port = 46101;
				string servername = GetFQDN();
				int naptime = 10000;
				ulong cn = 0;
				
				int unam=0;
				System.Text.RegularExpressions.Regex r = new System.Text.RegularExpressions.Regex("\\-([a-z])+=[\"']?(.*)[\"']?$");
				foreach (string arg in args) {
					var m = r.Match(arg);
					if (m.Success) {
						var g = m.Groups;
						switch (g[1].Value) {
							case "k":
								key = g[2].Value;
								break;
							case "s":
								server = g[2].Value;
								break;
							case "p":
								port =  Int32.Parse(g[2].Value);
								break;
							case "c":
								servername = g[2].Value;
								break;
							case "n":
								naptime =  Int32.Parse(g[2].Value);
								break;
							case "f":
								cn =  UInt64.Parse(g[2].Value);
								break;
								
						}
					} else {
						switch (unam) {
							case 0:
								server = arg;
								break;
							case 1:
								key = arg;
								break;		
							case 2:
								port =  Int32.Parse(arg);
								break;
							case 3:
								servername = arg;
								break;
						}
						unam++;
					}
				}
				
				System.Console.WriteLine(String.Format("Server {0};\nKey \t{1};\nPort \t{2}; \nClientName \t{3}",server,key,port,servername));
				
					
				
				
				var wb = new WebClient();
				if (cn == 0) {
					string cnqueryurl = String.Format("http://{0}:{1}/cnquery",server,port);
					
					var data = new NameValueCollection();
	    			data["key"] = key;
	    			data["server"] = servername;
	    			try {
	    				var response = wb.UploadValues(cnqueryurl, "POST", data);
	    				var a =  DeserializeAnswer(response);
	    				
	    				if (a.Success == "OK") {
	    					cn = a.Cn;
	    					Console.WriteLine("Fetched Cn "+cn);
	    				} else {
	    					throw (new Exception("Error while fetching Cn "+a.Error));
	    				}
	    			} catch (Exception e) {
	    				Console.WriteLine("Failure to fetch change number, can force by using -f=1 "+e.Message);
	    				Console.ReadKey();
	    			}
				} else {
					Console.WriteLine(String.Format("Forced cn {0}",cn));
				}

    			
    			if (cn != 0) {
	    			string url = String.Format("http://{0}:{1}/postproc",server,port);
					bool stopagent = false;
					
					
					while (!stopagent) {
						cn++;
						
						Console.WriteLine("Start collecting..");
						var x = new System.Xml.Serialization.XmlSerializer(typeof(UpdatePost));
						
						var writer = new Utf8StringWriter();
						x.Serialize(writer,new UpdatePost(ctr.getProc(),ctr.getServerUsage(),servername,cn));
						
						Console.WriteLine("Uploading..");
						
						var dataupd = new NameValueCollection();
	    				dataupd["key"] = key;
	    				dataupd["update"] = writer.ToString();
	
	    				try {
	    					var response = wb.UploadValues(url, "POST", dataupd);
					    	var uploadDataA = DeserializeAnswer(response);
					    	
					    	if (uploadDataA.Success == "STOP") {
					    		Console.WriteLine("Stopping");
					    		stopagent = true;
					    	} else  if (uploadDataA.Success == "OK") {
					    		Console.WriteLine("Succesful : "+uploadDataA.Success);
					    	} else {
					    		Console.WriteLine("Answer is unknown "+uploadDataA.Error);
					    		Console.WriteLine(System.Text.Encoding.UTF8.GetString(response));
								Console.WriteLine(string.Format("Data lost might be lost : {0}", dataupd["update"]));
					    	}
	    				} catch (Exception e) {
	    					Console.WriteLine("Failure to upload "+e.Message);
							Console.WriteLine(string.Format("Data lost : {0}", dataupd["update"]));
	    				}
	    				if (!stopagent) {
							System.Threading.Thread.Sleep(naptime);
	    				}
						
					}
    			}
			} else {
				Console.WriteLine("<exe> [[-s=]server] [[-k=]key] [[-p=]port] [-c=clientname]");
				Console.WriteLine("f.e <exe> localhost mysecretkey");
				Console.WriteLine("f.e <exe> -k=mysecretkey -s=localhost ");

			}
			
		}
	}
}