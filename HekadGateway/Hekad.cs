using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.RollingFile;
using StatsdClient;

namespace HekadGateway
{
	/// <summary>
	/// Extracts hekad binaries embedded in this assembly, configures and launches them. Note,
	/// that this WorkerRole must run as elevated.
	/// </summary>
	public sealed class Hekad
	{
		private readonly Thread _thread;

		private Hekad( Thread thread )
		{
			_thread = thread;
		}

		public static Hekad ConfigureAndLaunch(
			string logPath,
			string deployment,
			string instance,
			string serverUrl,
			string serverCertificateAuthority,
			string certificate,
			string privateKey,
			LoggerConfiguration loggerConfiguration,
			LogEventLevel minLevel = LogEventLevel.Information)
		{
			KillOldHekad();
			ConfigureStatsD( deployment, instance );
			ConfigureLogging( instance, deployment, logPath, loggerConfiguration, minLevel );

			var workingDir = GetTempFolder();
			ExtractResources( workingDir, serverCertificateAuthority, certificate, privateKey );

			var thread = new Thread( () => RunHekad( logPath, workingDir, serverUrl ) );
			thread.Start();

			return new Hekad( thread );
		}

		private static void RunHekad( string logPath, string workingFolder, string serverUrl )
		{
			var hekad = Log.ForContext< Hekad >();

			var list = new List< string >();
			// pipe output to log
			DataReceivedEventHandler log = ( sender, args ) =>
			{
				while( list.Count >= 100 )
					list.RemoveAt( 0 );
				list.Add( args.Data );
			};

			// lua plugins have weird path resolution, so we need to write config path explicitly

			var template = File.ReadAllText( Path.Combine( workingFolder, "hekad.template" ) )
				.Replace( "$WORKING_DIR$", workingFolder.Replace( '\\', '/' ) )
				.Replace( "$LOG_DIR$", logPath.Replace( '\\', '/' ) )
				.Replace( "$SERVER_URL$", serverUrl );
			const string configName = "hekad.toml";
			File.WriteAllText( Path.Combine( workingFolder, configName ), template );

			var hekadProcess = new Process
			{
				StartInfo =
				{
					UseShellExecute = false,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					CreateNoWindow = true,
					ErrorDialog = false,
					FileName = Path.Combine( workingFolder, "hekad.exe" ),
					WorkingDirectory = workingFolder,
					WindowStyle = ProcessWindowStyle.Hidden,
					Arguments = "--config=" + configName,
				},
				EnableRaisingEvents = false
			};
			try
			{
				hekadProcess.Start();

				hekadProcess.OutputDataReceived += log;
				hekadProcess.ErrorDataReceived += log;

				hekadProcess.BeginOutputReadLine();
				hekadProcess.BeginErrorReadLine();

				hekadProcess.WaitForExit();

				//hekad.Warning("process ended");
			}
			catch( Exception ex )
			{
				hekad.Fatal( "process aborted", ex );
			}
			finally
			{
				hekadProcess.OutputDataReceived -= log;
				hekadProcess.ErrorDataReceived -= log;

				// hekad shouldn't exit
				if( hekadProcess.HasExited )
				{
					hekad.Fatal( "Process terminated" );
					hekad.Information( string.Join( "\n", list ) );
				}
				else
					hekadProcess.Kill();
			}
		}

		public void Terminate()
		{
			if( _thread.IsAlive )
			{
				try
				{
					_thread.Abort();
				}
					// ReSharper disable EmptyGeneralCatchClause
				catch
					// ReSharper restore EmptyGeneralCatchClause
				{
				}
			}
			// do nothing for now.
		}

		private static void ConfigureLogging( string instance, string deployment, string logPath, LoggerConfiguration configuration, LogEventLevel minLevel )
		{
			var logFile = Path.Combine( logPath, "{Date}.log" );

			var formatter = new GelfFormatter( instance, deployment );
			var fileSink = new RollingFileSink( logFile, formatter, null, null, new UTF8Encoding( false ) );

			configuration.WriteTo.Sink( fileSink, restrictedToMinimumLevel: minLevel );
		}

		public static void ConfigureStatsD( string deployment, string instance )
		{
			deployment = deployment.Replace( ".", "-" );
			instance = instance.Replace( ".", "-" );
			var metricsConfig = new MetricsConfig
			{
				StatsdServerName = "localhost",
				Prefix = string.Format( "{0}.{1}", deployment, instance ),
				StatsdMaxUDPPacketSize = 512,
				StatsdServerPort = 8125,
			};

			Metrics.Configure( metricsConfig );
		}

		private static void KillOldHekad()
		{
			var running = Process.GetProcessesByName( "hekad" );
			foreach( var ps in running )
			{
				ps.Kill();
			}
		}

		private static string GetTempFolder()
		{
			var tempFolder = Path.GetTempPath();
			var full = Path.Combine( tempFolder, "hekad-bin" );
			if( !Directory.Exists( full ) )
				Directory.CreateDirectory( full );
			return full;
		}

		private static void ExtractResources( string full, string ca, string certificate, string privateKey )
		{
			var assembly = typeof( Hekad ).Assembly;

			File.WriteAllText( Path.Combine( full, "ca.crt" ), ca );
			File.WriteAllText( Path.Combine( full, "client.crt" ), certificate );
			File.WriteAllText( Path.Combine( full, "client.key" ), privateKey );

			var names = assembly.GetManifestResourceNames();
			const string prefix = "HekadGateway.hekad.";
			foreach( var name in names )
			{
				if( !name.StartsWith( prefix ) )
					throw new InvalidOperationException( "Unexpected resource " + name );
				var clean = name.Remove( 0, prefix.Length );

				var target = Path.Combine( full, clean );

				using( var stream = assembly.GetManifestResourceStream( name ) )
				{
					if( null == stream )
						throw new InvalidOperationException( "Empty stream for resource " + name );
					using( var file = File.Create( target ) )
						stream.CopyTo( file );
				}
			}
		}
	}
}