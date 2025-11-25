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

        // Моментальное отключение при бане + таймаут
        static void CheckDisconnectClient()
        {
            while (true)
            {
                for (int i = AllClients.Count - 1; i >= 0; i--)
                {
                    var client = AllClients[i];
                    bool isBlocked = IsUserBlocked(GetLoginByToken(client.Token));
                    int duration = (int)DateTime.Now.Subtract(client.DateConnect).TotalSeconds;

                    if (isBlocked || duration > Duration)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        string reason = isBlocked ? "blocked by admin" : "timeout";
                        Console.WriteLine($"Client disconnected: {client.Token} ({reason})");
                        AllClients.RemoveAt(i);
                    }
                }
                Thread.Sleep(1000);
            }
        }

        static void SetCommand()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            string cmd = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(cmd)) return;

            if (cmd == "/config")
            {
                File.Delete(".config");
                OnSettings();
            }
            else if (cmd.StartsWith("/block "))
            {
                string login = cmd.Substring(7).Trim();
                BlockUser(login, true);
                DisconnectUserByLogin(login); // Моментальное отключение
            }
            else if (cmd.StartsWith("/unblock "))
            {
                string login = cmd.Substring(9).Trim();
                BlockUser(login, false);
            }
            else if (cmd == "/status") GetStatus();
            else if (cmd == "/help") Help();
        }

        // Моментальное отключение по логину
        static void DisconnectUserByLogin(string login)
        {
            for (int i = AllClients.Count - 1; i >= 0; i--)
            {
                if (GetLoginByToken(AllClients[i].Token) == login)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Force disconnected: {login} (Token: {AllClients[i].Token})");
                    AllClients.RemoveAt(i);
                }
            }
        }

        static string GetLoginByToken(string token)
        {
            try
            {
                using var conn = new MySqlConnection(ConnectionString);
                conn.Open();
                string sql = "SELECT login FROM active_sessions WHERE token = @token";
                using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@token", token);
                var result = cmd.ExecuteScalar();
                return result?.ToString();
            }
            catch { return null; }
        }

        static bool IsUserBlocked(string login)
        {
            if (string.IsNullOrEmpty(login)) return false;
            try
            {
                using var conn = new MySqlConnection(ConnectionString);
                conn.Open();
                using var cmd = new MySqlCommand("SELECT is_blocked FROM users WHERE login = @login", conn);
                cmd.Parameters.AddWithValue("@login", login);
                var result = cmd.ExecuteScalar();
                return result != null && Convert.ToInt32(result) == 1;
            }
            catch { return false; }
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
                Console.WriteLine(rows > 0
                    ? $"User '{login}' {(block ? "BLOCKED" : "UNBLOCKED")}"
                    : "User not found");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("DB Error: " + ex.Message);
            }
        }

        static string ProcessClientMessage(string message)
        {
            if (!message.Contains(":")) return "/error Invalid format";

            var parts = message.Split(':');
            if (parts.Length < 2) return "/error Invalid format";

            string login = parts[0].Trim();
            string password = parts[1].Trim();
            bool isRegister = parts.Length > 2 && parts[2].Trim() == "register";

            try
            {
                using var conn = new MySqlConnection(ConnectionString);
                conn.Open();

                // Проверка на блокировку
                if (IsUserBlocked(login))
                    return "/error Blocked";

                // Регистрация
                if (isRegister)
                {
                    if (UserExists(login, conn))
                        return "/error Already exists";

                    string hash = BCrypt.Net.BCrypt.HashPassword(password);
                    string sql = "INSERT INTO users (login, password_hash, is_blocked) VALUES (@login, @hash, 0)";
                    using var cmd = new MySqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@login", login);
                    cmd.Parameters.AddWithValue("@hash", hash);
                    cmd.ExecuteNonQuery();

                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.WriteLine($"New user registered: {login}");
                    return "/success Registered! You can now login.";
                }

                // Авторизация
                if (!UserExists(login, conn))
                    return "/error Not found. Send 'login:pass:register' to create account.";

                string storedHash = GetPasswordHash(login, conn);
                if (!BCrypt.Net.BCrypt.Verify(password, storedHash))
                    return "/error Auth failed";

                if (AllClients.Count >= MaxClient)
                    return "/error Limit reached";

                var newClient = new Classes.Client();
                AllClients.Add(newClient);

                // Сохраняем сессию (для моментального отключения)
                SaveSession(login, newClient.Token, conn);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Authenticated: {login} → Token: {newClient.Token}");
                return newClient.Token;
            }
            catch (Exception ex)
            {
                Console.WriteLine("DB Exception: " + ex.Message);
                return "/error DB error";
            }
        }

        static bool UserExists(string login, MySqlConnection conn)
        {
            using var cmd = new MySqlCommand("SELECT 1 FROM users WHERE login = @login", conn);
            cmd.Parameters.AddWithValue("@login", login);
            return cmd.ExecuteScalar() != null;
        }

        static string GetPasswordHash(string login, MySqlConnection conn)
        {
            using var cmd = new MySqlCommand("SELECT password_hash FROM users WHERE login = @login", conn);
            cmd.Parameters.AddWithValue("@login", login);
            return (string)cmd.ExecuteScalar();
        }



        static void SaveSession(string login, string token, MySqlConnection conn)
        {
            string sql = "REPLACE INTO active_sessions (login, token, last_seen) VALUES (@login, @token, NOW())";
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@login", login);
            cmd.Parameters.AddWithValue("@token", token);
            cmd.ExecuteNonQuery();
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

                    string Response;

                    // Это проверка токена (не логин/пароль)
                    if (!Message.Contains(":"))
                    {
                        var client = AllClients.Find(c => c.Token == Message);
                        if (client == null)
                        {
                            Response = "/disconnect"; // старый токен
                        }
                        else
                        {
                            string login = GetLoginByToken(Message);
                            if (IsUserBlocked(login))
                            {
                                // УДАЛЯЕМ ИЗ СПИСКА И ГОВОРИМ КЛИЕНТУ, ЧТО ОН ЗАБАНЕН
                                AllClients.Remove(client);
                                Response = "/blocked"; // ← НОВОЕ СООБЩЕНИЕ
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine($"Клиент заблокирован и отключён: {login} (Token: {Message})");
                            }
                            else
                            {
                                Response = "/ok"; // токен валиден
                                                  // Обновляем время активности
                                using var conn = new MySqlConnection(ConnectionString);
                                conn.Open();
                                using var cmd = new MySqlCommand("UPDATE active_sessions SET last_seen = NOW() WHERE token = @token", conn);
                                cmd.Parameters.AddWithValue("@token", Message);
                                cmd.ExecuteNonQuery();
                            }
                        }
                    }
                    else
                    {
                        // Это логин/пароль — обрабатываем как раньше
                        Response = ProcessClientMessage(Message);
                    }

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