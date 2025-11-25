using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using MySql.Data.MySqlClient;
using BCrypt.Net;  // Для BCrypt.Net-Next

namespace Server
{
    class Program
    {
        static IPAddress ServerIpAddress;
        static int ServerPort;
        static int MaxClient;
        static int Duration;
        static List<Classes.Client> AllClients = new List<Classes.Client>();
        static string ConnectionString = "Server=localhost;Database=LicenseDB;Uid=root;Pwd=;Charset=utf8;SslMode=Preferred;AllowPublicKeyRetrieval=true;";

        static void Main(string[] args)
        {
            

            OnSettings();
            Thread tListener = new Thread(ConnectServer);
            tListener.Start();
            Thread tDisconnect = new Thread(CheckDisconnectClient);
            tDisconnect.Start();
            while (true)
                SetCommand();
        }

        static void CheckDisconnectClient()
        {
            while (true)
            {
                for (int i = AllClients.Count - 1; i >= 0; i--)
                {
                    int ClientDuration = (int)DateTime.Now.Subtract(AllClients[i].DateConnect).TotalSeconds;
                    if (ClientDuration > Duration)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Client: {AllClients[i].Token} disconnected (timeout)");
                        AllClients.RemoveAt(i);
                    }
                }
                Thread.Sleep(1000);
            }
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
            else if (Command.StartsWith("/block "))
            {
                string login = Command.Substring(7).Trim();
                BlockUser(login, true);
            }
            else if (Command.StartsWith("/unblock "))
            {
                string login = Command.Substring(9).Trim();
                BlockUser(login, false);
            }
            else if (Command == "/status") GetStatus();
            else if (Command == "/help") Help();
            else
            {
                Console.WriteLine("Unknown command. Use /help");
            }
        }

        static void BlockUser(string login, bool block)
        {
            try
            {
                using var conn = new MySqlConnection(ConnectionString);
                conn.Open();
                string sql = "UPDATE users SET is_blocked = @blocked WHERE login = @login";
                using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@blocked", block ? 1 : 0);
                cmd.Parameters.AddWithValue("@login", login);
                int rows = cmd.ExecuteNonQuery();
                Console.ForegroundColor = block ? ConsoleColor.Red : ConsoleColor.Green;
                Console.WriteLine(rows > 0 ? $"User {login} {(block ? "added to blacklist" : "removed from blacklist")}" : "User not found");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("DB Error: " + ex.Message);
            }
        }

        static string ProcessClientMessage(string message)
        {
            if (!message.Contains(":"))
                return "/error Invalid format";

            var parts = message.Split(':');
            if (parts.Length != 2) return "/error Invalid format";
            string login = parts[0].Trim();
            string password = parts[1].Trim();

            try
            {
                using var conn = new MySqlConnection(ConnectionString);
                conn.Open();

                // 1. Проверка на чёрный список (blacklist)
                string checkBlockSql = "SELECT is_blocked FROM users WHERE login = @login";
                using (var checkCmd = new MySqlCommand(checkBlockSql, conn))
                {
                    checkCmd.Parameters.AddWithValue("@login", login);
                    var blockedResult = checkCmd.ExecuteScalar();
                    if (blockedResult != null && Convert.ToInt32(blockedResult) == 1)
                    {
                        return "/error Blocked";  // Клиент в чёрном списке
                    }
                }

                // 2. Проверка логина и пароля
                string authSql = "SELECT password_hash FROM users WHERE login = @login";
                using var authCmd = new MySqlCommand(authSql, conn);
                authCmd.Parameters.AddWithValue("@login", login);
                var hashResult = authCmd.ExecuteScalar();
                if (hashResult == null)
                    return "/error User not found";

                string storedHash = (string)hashResult;
                if (!BCrypt.Net.BCrypt.Verify(password, storedHash))
                    return "/error Auth failed";

                // 3. Проверка лимита клиентов
                if (AllClients.Count >= MaxClient)
                    return "/error Limit reached";

                // 4. Успех: генерируем уникальный токен
                var newClient = new Classes.Client();
                AllClients.Add(newClient);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Authenticated: {login} → Token: {newClient.Token}");
                return newClient.Token;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("DB Exception: " + ex.Message);
                return "/error DB error";
            }
        }

        static void ConnectServer()
        {
            IPEndPoint EndPoint = new IPEndPoint(ServerIpAddress, ServerPort);
            Socket SocketListener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            SocketListener.Bind(EndPoint);
            SocketListener.Listen(10);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Server running on {ServerIpAddress}:{ServerPort}. Max clients: {MaxClient}, Token lifetime: {Duration}s");

            while (true)
            {
                try
                {
                    Socket Handler = SocketListener.Accept();
                    byte[] Bytes = new byte[10485760];
                    int ByteRec = Handler.Receive(Bytes);
                    string Message = Encoding.UTF8.GetString(Bytes, 0, ByteRec).Trim();
                    string Response = ProcessClientMessage(Message);
                    Handler.Send(Encoding.UTF8.GetBytes(Response));
                    Handler.Shutdown(SocketShutdown.Both);
                    Handler.Close();
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Connection error: " + ex.Message);
                }
            }
        }

        static void GetStatus()
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"Connected clients: {AllClients.Count}/{MaxClient}");
            foreach (Classes.Client Client in AllClients)
            {
                int DurationSec = (int)DateTime.Now.Subtract(Client.DateConnect).TotalSeconds;
                Console.WriteLine($"Token: {Client.Token}, Connected: {Client.DateConnect:HH:mm:ss dd.MM}, Duration: {DurationSec}s");
            }
        }

        static void Help()
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Server commands:");
            Console.WriteLine("/config        — reset settings");
            Console.WriteLine("/block <login> — add user to blacklist (in DB)");
            Console.WriteLine("/unblock <login> — remove from blacklist");
            Console.WriteLine("/status        — show connected clients");
            Console.WriteLine("/help          — this help");
        }

        static void OnSettings()
        {
            string Path = Directory.GetCurrentDirectory() + "/.config";
            if (File.Exists(Path))
            {
                string[] lines = File.ReadAllLines(Path);
                ServerIpAddress = IPAddress.Parse(lines[0]);
                ServerPort = int.Parse(lines[1]);
                MaxClient = int.Parse(lines[2]);
                Duration = int.Parse(lines[3]);
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine($"Loaded: IP={ServerIpAddress}, Port={ServerPort}, MaxClients={MaxClient}, Duration={Duration}s");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("Server IP: ");
                string ipStr = Console.ReadLine();
                ServerIpAddress = IPAddress.Parse(ipStr);
                Console.Write("Port: ");
                ServerPort = int.Parse(Console.ReadLine());
                Console.Write("Max clients: ");
                MaxClient = int.Parse(Console.ReadLine());
                Console.Write("Token lifetime (seconds): ");
                Duration = int.Parse(Console.ReadLine());
                File.WriteAllLines(Path, new[] { ipStr, ServerPort.ToString(), MaxClient.ToString(), Duration.ToString() });
            }
            Console.WriteLine("Use /config to change.");
        }
    }
}