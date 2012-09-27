/*
 * monod.cs: Mono daemon for running services based on System.ServiceProcess
 *
 * Author:
 *   Joerg Rosenkranz (joergr@voelcker.com)
 *   Miguel de Icaza (miguel@novell.com)
 *
 * (C) 2005 Voelcker Informatik AG
 * (C) 2005 Novell Inc
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Mono.Unix;
using Mono.Unix.Native;
using System.ServiceProcess;
using System.Threading;
using System.Runtime.InteropServices;

class MonoServiceRunner : MarshalByRefObject
{
	static string output;

	string assembly, name, logname, minThreads, maxThreads;
	bool exclude_sigusr;
	string[] args;
	
	static void info (string prefix, string format, params object [] args)
	{
		var msg = String.Format ("{0}: {1}", prefix, String.Format (format, args));
		switch (output){
		case null: Syscall.syslog (SyslogLevel.LOG_NOTICE, msg); break;
		case "stdout": Console.WriteLine (msg); break;
		default: Console.Error.WriteLine (msg); break;
		}
	}
	
	static void error (string prefix, string format, params object [] args)
	{
		var msg = String.Format ("{0}: {1}", prefix, String.Format (format, args));
		switch (output){
		case null: Syscall.syslog (SyslogLevel.LOG_ERR, msg); break;
		case "stdout": Console.WriteLine (msg); break;
		default: Console.Error.WriteLine (msg); break;
		}
	}
	
	static void Usage ()
	{
		Console.Error.WriteLine (
					 "Usage is:\n" +
					 "mono-service [-d:DIRECTORY] [-l:LOCKFILE] [-n:NAME] [-m:LOGNAME]\n" +
					 "[-i:MIN_WORKER_THREADS[:MIN_IO_THREADS]] [-a:MAX_WORKER_THREADS[:MAX_IO_THREADS]]\n" +
					 "[-o:OUTPUT] [-x:BOOL] service.exe\n");
		Environment.Exit (1);
	}

	static void call (object o, string method, object [] arg)
	{
		MethodInfo m = o.GetType ().GetMethod (method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
		if (arg != null)
			m.Invoke (o, new object [1] { arg });
		else
			m.Invoke (o, null);
	}
	
	static int Main (string [] args)
	{
		string assembly = null;
		string directory = null;
		string lockfile = null;
		string name = null;
		string logname = null;
		string minThreads = null;
		string maxThreads = null;
		string output = null;
		bool exclude_sigusr = false;
		var assebmlyArgs = new List<string>();

		foreach (string s in args){
			if (s.Length > 3 && s [0] == '-' && s [2] == ':'){
				string arg = s.Substring (3);

				switch (Char.ToLower (s [1])){
				case 'd': directory = arg; break;
				case 'l': lockfile = arg; break;
				case 'n': name = arg; break;
				case 'm': logname = arg; break;
				case 'i': minThreads = arg; break;
				case 'a': maxThreads = arg; break;
				case 'o': output = arg; break;
				case 'x': exclude_sigusr = (arg == "true"); break;
				default: Usage (); break;
				}
			} else {
				if (assembly != null)
				{
					assebmlyArgs.Add(s);
				}
				else
				{
					assembly = s;
				}
			}
		}

		if (logname == null)
			logname = assembly;

		if (assembly == null){
			error (logname, "Assembly name is missing");
			Usage ();
		}
		
		if (directory != null){
			if (Syscall.chdir (directory) != 0){
				error (logname, "Could not change to directory {0}", directory);
				return 1;
			}
		}
		
		// Use lockfile to allow only one instance
		if (lockfile == null)
			lockfile = String.Format ("/tmp/{0}.lock", Path.GetFileName (assembly));

		int lfp = Syscall.open (lockfile, OpenFlags.O_RDWR|OpenFlags.O_CREAT|OpenFlags.O_EXCL, 
			FilePermissions.S_IRUSR|FilePermissions.S_IWUSR|FilePermissions.S_IRGRP);

		if (lfp<0)  {
		        // Provide some useful info
			if (File.Exists (lockfile))
				error (logname, String.Format ("Lock file already exists: {0}", lockfile));
			else 
				error (logname, String.Format ("Cannot open/create lock file exclusively: {0}", lockfile));
			return 1;
		}
	
		if (Syscall.lockf(lfp, LockfCommand.F_TLOCK,0)<0)  {
			info (logname, "Daemon is already running.");
			return 0;
		}
		
		try {
			// Write pid to lock file
			string pid = Syscall.getpid ().ToString () + Environment.NewLine;
			IntPtr buf = Marshal.StringToCoTaskMemAnsi (pid);
			Syscall.write (lfp, buf, (ulong)pid.Length);
			Marshal.FreeCoTaskMem (buf);
	
			// Create new AppDomain to run service
			AppDomainSetup setup = new AppDomainSetup ();
			setup.ApplicationBase = Environment.CurrentDirectory;
			setup.ConfigurationFile = Path.Combine (Environment.CurrentDirectory, assembly + ".config");
			setup.ApplicationName = logname;
			
			AppDomain newDomain = AppDomain.CreateDomain (logname, AppDomain.CurrentDomain.Evidence, setup);
			MonoServiceRunner rnr = newDomain.CreateInstanceAndUnwrap(
				typeof (MonoServiceRunner).Assembly.FullName,
				typeof (MonoServiceRunner).FullName,
				true,
				BindingFlags.Default,
				null,
				new object [] {assembly, name, logname, minThreads, maxThreads,
					       output, exclude_sigusr, assebmlyArgs.ToArray()},
				null, null, null) as MonoServiceRunner;
				
			if (rnr == null) {
				error (logname, "Internal Mono Error: Could not create MonoServiceRunner.");
				return 1;
			}
	
			return rnr.StartService ();
		} finally {
			// Remove lock file when done
			if (File.Exists(lockfile))
				File.Delete (lockfile);
		}
	}
	
	public MonoServiceRunner (string assembly, string name, string logname,
				  string minThreads, string maxThreads,
				  string output_name, bool exclude_sigusr, string[] args)
	{
		output = output_name;

		this.assembly = assembly;
		this.name = name;
		this.logname = logname;
		this.minThreads = minThreads;
		this.maxThreads = maxThreads;
		this.exclude_sigusr = exclude_sigusr;
		this.args = args;
	}
	
	public int StartService ()
	{
		try	{
			// Load service assembly
			Assembly a = null;
			
			try {
				a = Assembly.LoadFrom (assembly);
			} catch (FileNotFoundException) {
				error (logname, "Could not find assembly {0}", assembly);
				return 1;
			} catch (BadImageFormatException){
				error (logname, "File {0} is not a valid assembly", assembly);
				return 1;
			} catch { }
			
			if (a == null){
				error (logname, "Could not load assembly {0}", assembly);
				return 1;
			}
			
			if (a.EntryPoint == null){
				error (logname, "Entry point not defined in service");
				return 1;
			}
			
			// Hook up RunService callback
			Type cbType = Type.GetType ("System.ServiceProcess.ServiceBase+RunServiceCallback, System.ServiceProcess");
			if (cbType == null){
				error (logname, "Internal Mono Error: Could not find RunServiceCallback in ServiceBase");
				return 1;			
			}
			
			FieldInfo fi = typeof (ServiceBase).GetField ("RunService", BindingFlags.Static | BindingFlags.NonPublic);
			if (fi == null){
				error (logname, "Internal Mono Error: Could not find RunService in ServiceBase");
				return 1;
			}
			fi.SetValue (null, Delegate.CreateDelegate(cbType, this, "MainLoop"));
			
			// And run its Main. Our RunService handler is invoked from 
			// ServiceBase.Run.
			return AppDomain.CurrentDomain.ExecuteAssembly (assembly, AppDomain.CurrentDomain.Evidence, args);
			
		} catch ( Exception ex ) {
			var splitStrings = new string[] {Environment.NewLine};
			for (Exception e = ex; e != null; e = e.InnerException) {
				error (logname, e.Message);
				foreach (var line in e.StackTrace.Split(splitStrings, StringSplitOptions.RemoveEmptyEntries))
				{
					error (logname, line);
				}
			}
			
			return 1;
		}
	}
	
	// The main service loop
	private void MainLoop (ServiceBase [] services)
	{
		try {
			ServiceBase service;

			if (services == null || services.Length == 0){
				error (logname, "No services were registered by this service");
				return;
			}

			// Optionally adjust the service's thread limits.
			if (this.maxThreads != null){
				int[] maxThreads = { 0, 0 };
				string[] limits = this.maxThreads.Split (':');
				if (limits.Length > 0)
					maxThreads[0] = Convert.ToInt32 (limits[0]);
				if (limits.Length > 1)
					maxThreads[1] = Convert.ToInt32 (limits[1]);
				else
					maxThreads[1] = maxThreads[0];

				if (!ThreadPool.SetMaxThreads(maxThreads[0], maxThreads[1])) {
					error (logname, "The value given for maxThreads is not valid: {0}:{1}", maxThreads[0], maxThreads[1]);
				}
			}

			if (this.minThreads != null){
				int[] minThreads = { 0, 0 };
				string[] limits = this.minThreads.Split (':');
				if (limits.Length > 0)
					minThreads[0] = Convert.ToInt32 (limits[0]);
				if (limits.Length > 1)
					minThreads[1] = Convert.ToInt32 (limits[1]);
				else
					minThreads[1] = minThreads[0];

				if (!ThreadPool.SetMinThreads(minThreads[0], minThreads[1])) {
					error (logname, "The value given for minThreads is not valid: {0}:{1}", minThreads[0], minThreads[1]);
				}
			}
			
			// Start up the service.
			service = null;
			
			if (name != null){
				foreach (ServiceBase svc in services){
					if (svc.ServiceName == name){
						service = svc;
						break;
					}
				}
			} else {
				service = services [0];
			}
	
			call (service, "OnStart", args);
			info (logname, "Service {0} started", service.ServiceName);
	
			UnixSignal intr = new UnixSignal (Signum.SIGINT);
			UnixSignal term = new UnixSignal (Signum.SIGTERM);
			UnixSignal usr1 = null;
			UnixSignal usr2 = null;

			UnixSignal[] sigs = null;
			if (exclude_sigusr){
				sigs = new UnixSignal[]{
					intr,
					term
				};
				info (logname, "Ignoring USR signals");
			} else {
				usr1 = new UnixSignal (Signum.SIGUSR1);
				usr2 = new UnixSignal (Signum.SIGUSR2);
				sigs = new UnixSignal[]{
					intr,
					term,
					usr1,
					usr2
				};
			}

			for (bool running = true; running; ){
				int idx = UnixSignal.WaitAny (sigs);
				if (idx < 0 || idx >= sigs.Length)
					continue;
				if ((intr.IsSet || term.IsSet) && service.CanStop) {
					intr.Reset ();
					term.Reset ();
					info (logname, "Stopping service {0}", service.ServiceName);
					call (service, "OnStop", null);
					running = false;
				}
				else if (usr1 != null && usr1.IsSet && service.CanPauseAndContinue) {
					usr1.Reset ();
					info (logname, "Pausing service {0}", service.ServiceName);
					call (service, "OnPause", null);
				}
				else if (usr2 != null && usr2.IsSet && service.CanPauseAndContinue) {
					usr2.Reset ();
					info (logname, "Continuing service {0}", service.ServiceName);
					call (service, "OnContinue", null);
				}
			}
		} finally {
			// Clean up
			foreach (ServiceBase svc in services){
				svc.Dispose ();
			}
		}
	}
}
