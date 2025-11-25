using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Client
{
    class Program
    {
        static IPAddress ServerIpAddress;
        static int ServerPort;
        static string ClientToken;
        static DateTime ClientDateConnection;

        static void Main(string[] args)
        {
            OnSettings();
            Thread tCheckToken = new Thread(CheckToken);
            tCheckToken.Start();
            while (true)
                SetCommand();
        }

        static void CheckToken()
        {
            while (true)
            {
                if (!string.IsNullOrEmpty(ClientToken))
                {
                    IPEndPoint EndPoint = new IPEndPoint(ServerIpAddress, ServerPort);
                    using Socket Socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        Socket.Connect(EndPoint);
                        Socket.Send(Encoding.UTF8.GetBytes(ClientToken));
                        byte[] Bytes = new byte[10485760];
                        int ByteRec = Socket.Receive(Bytes);
                        string Response = Encoding.UTF8.GetString(Bytes, 0, ByteRec).Trim();
                        if (Response == "/disconnect")
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("Disconnected from server (invalid token)");
                            ClientToken = string.Empty;
                        }
                    }
                    catch (Exception exp)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("Token check error: " + exp.Message);
                    }
                }
                Thread.Sleep(1000);
            }
        }

        static void ConnectServer()
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("Login: ");
            string login = Console.ReadLine().Trim();
            Console.Write("Password: ");
            string password = ReadPassword();

            string credentials = login + ":" + password;

            IPEndPoint EndPoint = new IPEndPoint(ServerIpAddress, ServerPort);
            using Socket Socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            try
            {
                Socket.Connect(EndPoint);
                Socket.Send(Encoding.UTF8.GetBytes(credentials));
                byte[] Bytes = new byte[10485760];
                int ByteRec = Socket.Receive(Bytes);
                string Response = Encoding.UTF8.GetString(Bytes, 0, ByteRec).Trim();

                if (Response.StartsWith("/error"))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    switch (Response)
                    {
                        case "/error Blocked":
                            Console.WriteLine("Error: You are in the blacklist! Contact admin.");
                            break;
                        case "/error Auth failed":
                        case "/error User not found":
                            Console.WriteLine("Error: Wrong login or password!");
                            break;
                        case "/error Limit reached":
                            Console.WriteLine("Error: No free licenses on server!");
                            break;
                        case "/error Invalid format":
                            Console.WriteLine("Error: Invalid login/password format!");
                            break;
                        default:
                            Console.WriteLine("Error: " + Response);
                            break;
                    }
                }
                else
                {
                    ClientToken = Response;
                    ClientDateConnection = DateTime.Now;
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Success! Token: {ClientToken} (Valid for {DateTime.Now.AddSeconds(300):HH:mm:ss})");  // Пример, подставь Duration если нужно
                }
            }
            catch (Exception exp)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Connection error: " + exp.Message);
            }
        }

        static string ReadPassword()
        {
            string password = "";
            while (true)
            {
                ConsoleKeyInfo key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Enter)
                    break;
                if (key.Key == ConsoleKey.Backspace && password.Length > 0)
                {
                    password = password[..^1];
                    Console.Write("\b \b");
                }
                else if (key.KeyChar != '\0')
                {
                    password += key.KeyChar;
                    Console.Write("*");
                }
            }
            Console.WriteLine();
            return password;
        }

        static void GetStatus()
        {
            if (string.IsNullOrEmpty(ClientToken))
            {
                Console.WriteLine("Not connected.");
                return;
            }
            int Duration = (int)DateTime.Now.Subtract(ClientDateConnection).TotalSeconds;
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"Token: {ClientToken}, Connected: {ClientDateConnection:HH:mm:ss dd.MM}, Duration: {Duration}s");
        }

        static void SetCommand()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            string Command = Console.ReadLine();
            if (Command == "/config")
            {
                File.Delete(Directory.GetCurrentDirectory() + "/.config");
                OnSettings();
            }
            else if (Command == "/connect") ConnectServer();
            else if (Command == "/status") GetStatus();
            else if (Command == "/help") Help();
            else
            {
                Console.WriteLine("Unknown command. Use /help");
            }
        }

        static void Help()
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Client commands:");
            Console.WriteLine("/config  — reset settings");
            Console.WriteLine("/connect — connect with login/password");
            Console.WriteLine("/status  — show connection status");
            Console.WriteLine("/help    — this help");
        }

        static void OnSettings()
        {
            string Path = Directory.GetCurrentDirectory() + "/.config";
            if (File.Exists(Path))
            {
                string[] lines = File.ReadAllLines(Path);
                ServerIpAddress = IPAddress.Parse(lines[0]);
                ServerPort = int.Parse(lines[1]);
                Console.WriteLine($"Loaded: IP={ServerIpAddress}, Port={ServerPort}");
            }
            else
            {
                Console.Write("Server IP: ");
                string ipStr = Console.ReadLine();
                ServerIpAddress = IPAddress.Parse(ipStr);
                Console.Write("Port: ");
                ServerPort = int.Parse(Console.ReadLine());
                File.WriteAllLines(Path, new[] { ipStr, ServerPort.ToString() });
            }
            Console.WriteLine("Use /config to change.");
        }
    }
}