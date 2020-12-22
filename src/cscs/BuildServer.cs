using System;
using static System.Console;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// using compile_server;

// using csscript;

namespace CSScripting.CodeDom
{
    public static partial class BuildServer
    {
        static string csc_asm_file;

        static public string csc
        {
            get
            {
                if (csc_asm_file == null)
                {
                    // linux ~dotnet/.../3.0.100-preview5-011568/Roslyn/... (cannot find in preview)
                    // win: program_files/dotnet/sdk/<version>/Roslyn/csc.dll
                    var dotnet_root = "".GetType().Assembly.Location;

                    // find first "dotnet" parent dir by trimming till the last "dotnet" token
                    dotnet_root = String.Join(Path.DirectorySeparatorChar,
                                              dotnet_root.Split(Path.DirectorySeparatorChar)
                                                         .Reverse()
                                                         .SkipWhile(x => x != "dotnet")
                                                         .Reverse()
                                                         .ToArray());

                    var sdkDir = Path.Combine(dotnet_root, "sdk");
                    if (Directory.Exists(sdkDir)) // need to check as otherwise it will throw
                    {
                        var dirs = Directory.GetDirectories(sdkDir)
                                            .Where(dir => { var firstChar = Path.GetFileName(dir)[0]; return char.IsDigit(firstChar); })
                                            .OrderBy(x => Version.Parse(Path.GetFileName(x).Split('-').First()))
                                            .ThenBy(x => Path.GetFileName(x).Split('-').Count())
                                            .SelectMany(dir => Directory.GetDirectories(dir, "Roslyn"))
                                            .ToArray();

                        csc_asm_file = dirs.Select(dir => Path.Combine(dir, "bincore", "csc.dll"))
                                       .LastOrDefault(File.Exists);
                    }
                }
                return csc_asm_file;
            }
        }

        public static int serverPort = 17001;

        static public string Request(string request, int? port)
        {
            using var clientSocket = new TcpClient();
            clientSocket.Connect(IPAddress.Loopback, port ?? serverPort);
            clientSocket.WriteAllBytes(request.GetBytes());
            return clientSocket.ReadAllBytes().GetString();
        }

        static public string SendBuildRequest(string[] args, int? port)
        {
            try
            {
                // first arg is the compiler identifier: csc|vbc

                string request = string.Join('\n', args.Skip(1));
                string response = BuildServer.Request(request, port);

                return response;
            }
            catch (Exception e)
            {
                return e.ToString();
            }
        }

        static public bool IsServerAlive(int? port)
        {
            try
            {
                BuildServer.Request("-ping", port);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void EnsureServerRunning(int? port)
        {
            if (!IsServerAlive(port))
                StartRemoteInstance(port);
        }

        public static void StartRemoteInstance(int? port)
        {
            try
            {
                System.Diagnostics.Process proc = new();

                proc.StartInfo.FileName = "dotnet";
                proc.StartInfo.Arguments = $"{Assembly.GetExecutingAssembly().Location} -listen -port:{port}";
                proc.StartInfo.UseShellExecute = false;
                proc.StartInfo.RedirectStandardOutput = true;
                proc.StartInfo.RedirectStandardError = true;
                proc.StartInfo.CreateNoWindow = true;
                proc.Start();
            }
            catch { }
        }

        public static string StopRemoteInstance(int? port)
        {
            try
            {
                return "-stop".SendTo(IPAddress.Loopback, port ?? serverPort);
            }
            catch { return "<no respone>"; }
        }

        public static string PingRemoteInstance(int? port)
        {
            try
            {
                return "-ping".SendTo(IPAddress.Loopback, port ?? serverPort);
            }
            catch { return "<no respone>"; }
        }

        public static string PingRemoteInstances()
        {
            try
            {
                var buf = new StringBuilder();

                if (Directory.Exists(build_server_active_instances))
                    foreach (string activeServer in Directory.GetFiles(build_server_active_instances, "*.pid"))
                    {
                        var proc = GetProcess(int.Parse(Path.GetFileNameWithoutExtension(activeServer)));
                        if (proc != null)
                            buf.AppendLine($"pid:{proc.Id}, port:{File.ReadAllText(activeServer).ParseAsPort()}");
                    }

                return buf.ToString();
                //"-ping".SendTo(IPAddress.Loopback, port ?? serverPort);
            }
            catch { return "<no respone>"; }
        }

        public static void SimulateCloseSocketSignal()
            => closeSocketRequested = true;

        public static void SimulateCloseAppSignal()
        {
            var mutex = new Mutex(true, "cs-script.build_server.shutdown");
        }

        static bool closeSocketRequested = false;

        static internal string build_server_active_instances
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "cs-script",
                            "bin",
                            "compiler",
                            "active");

        public static Process GetProcess(int id)
        {
            try
            {
                return Process.GetProcesses().FirstOrDefault(x => x.Id == id);
            }
            catch
            {
            }
            return null;
        }

        public static void ReportRunning(int? port)
        {
            Directory.CreateDirectory(build_server_active_instances);
            var pidFile = Path.Combine(build_server_active_instances, $"{Process.GetCurrentProcess().Id}.pid");
            File.WriteAllText(pidFile, (port ?? serverPort).ToString());
        }

        public static void PurgeRunningHistory()
        {
            try
            {
                if (Directory.Exists(build_server_active_instances))
                    foreach (string activeServer in Directory.GetFiles(build_server_active_instances, "*.pid"))
                    {
                        var proc = GetProcess(int.Parse(Path.GetFileNameWithoutExtension(activeServer)));
                        if (proc == null)
                            File.Delete(activeServer);
                    }
            }
            catch { }
        }

        public static int? ParseAsPort(this string data)
        {
            if (string.IsNullOrEmpty(data))
                return null;
            else
                return int.Parse(data);
        }

        public static void ReportExit()
        {
            var pidFile = Path.Combine(build_server_active_instances, $"{Process.GetCurrentProcess().Id}.pid");

            if (File.Exists(pidFile))
                File.Delete(pidFile);
        }

        // public static void KillAllInstances()
        // {
        //     if (Directory.Exists(build_server_active_instances))
        //         foreach (string activeServer in Directory.GetFiles(build_server_active_instances, "*.pid"))
        //         {
        //             var proc = GetProcess(int.Parse(Path.GetFileNameWithoutExtension(activeServer)));
        //             try
        //             {
        //                 proc?.Kill();
        //                 File.Delete(activeServer);
        //             }
        //             catch { }
        //         }
        // }

        public static void ListenToRequests(int? port)
        {
            var serverSocket = new TcpListener(IPAddress.Loopback, port ?? serverPort);
            try
            {
                serverSocket.Start();

                while (true)
                {
                    using (TcpClient clientSocket = serverSocket.AcceptTcpClient())
                    {
                        try
                        {
                            string request = clientSocket.ReadAllText();

                            if (request == "-stop")
                            {
                                try { clientSocket.WriteAllText($"Terminating pid:{Process.GetCurrentProcess().Id}"); } catch { }
                                break;
                            }
                            else if (request == "-ping")
                            {
                                try { clientSocket.WriteAllText($"pid:{Process.GetCurrentProcess().Id}"); } catch { }
                            }
                            else
                            {
                                string response = CompileWithCsc(request.Split('\n'));

                                clientSocket.WriteAllText(response);
                            }
                        }
                        catch (Exception e)
                        {
                            WriteLine(e.Message);
                        }
                    }

                    Task.Run(PurgeRunningHistory);
                }

                serverSocket.Stop();
                WriteLine(" >> exit");
            }
            catch (SocketException e)
            {
                if (e.ErrorCode == 10048)
                    WriteLine(">" + e.Message);
                else
                    WriteLine(e.Message);
            }
            catch (Exception e)
            {
                WriteLine(e);
            }
        }

        static string CompileWithCsc(string[] args)
        {
            using (SimpleAsmProbing.For(Path.GetDirectoryName(csc)))
            {
                var oldOut = Console.Out;
                using StringWriter buff = new();

                Console.SetOut(buff);

                try
                {
                    AppDomain.CurrentDomain.ExecuteAssembly(csc, args);
                }
                catch (Exception e)
                {
                    return e.ToString();
                }
                finally
                {
                    Console.SetOut(oldOut);
                }
                return buff.GetStringBuilder().ToString();
            }
        }
    }
}