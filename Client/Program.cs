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
                            Console.WriteLine("Лицензия истекла или токен недействителен.");
                            ClientToken = string.Empty;
                        }
                        else if (Response == "/blocked")
                        {
                            Console.ForegroundColor = ConsoleColor.DarkRed;
                            Console.BackgroundColor = ConsoleColor.Yellow;
                            Console.WriteLine();
                            Console.WriteLine("==========================================");
                            Console.WriteLine("   ВЫ ЗАБЛОКИРОВАНЫ АДМИНИСТРАТОРОМ!   ");
                            Console.WriteLine("   Токен аннулирован. Свяжитесь с админом.");
                            Console.WriteLine("==========================================");
                            Console.ResetColor();
                            ClientToken = string.Empty;
                            Thread.Sleep(Timeout.Infinite);
                        }
                    }
                    catch (Exception exp)
                    {
                        Thread.Sleep(2000);
                    }
                }
                Thread.Sleep(1000);
            }
        }

        static void ConnectServer()
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("=== АВТОРИЗАЦИЯ / РЕГИСТРАЦИЯ ===");
            Console.WriteLine("Введите логин (или логин:пароль:register для регистрации)");
            Console.Write("┌─ Login: ");
            string input = Console.ReadLine()?.Trim();
            Console.Write("└─ ");

            if (string.IsNullOrEmpty(input))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Логин не может быть пустым!");
                return;
            }

            string login, password;
            bool isRegister = false;
            if (input.EndsWith(":register") && input.Contains(":"))
            {
                var parts = input.Split(':');
                if (parts.Length >= 3)
                {
                    login = parts[0].Trim();
                    password = parts[1].Trim();
                    isRegister = true;
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.WriteLine($"→ Режим: РЕГИСТРАЦИЯ нового пользователя '{login}'");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Формат регистрации: логин:пароль:register");
                    return;
                }
            }
            else if (input.Contains(":"))
            {
                var parts = input.Split(':');
                login = parts[0].Trim();
                password = parts.Length > 1 ? parts[1].Trim() : "";
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"→ Режим: ВХОД под пользователем '{login}'");
            }
            else
            {
                login = input;
                Console.Write("   Password: ");
                password = ReadPassword();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"→ Режим: ВХОД под пользователем '{login}'");
            }

            if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Логин и пароль обязательны!");
                return;
            }

            string credentials = isRegister ? $"{login}:{password}:register" : $"{login}:{password}";

            IPEndPoint EndPoint = new IPEndPoint(ServerIpAddress, ServerPort);
            using Socket Socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            try
            {
                Socket.Connect(EndPoint);
                Socket.Send(Encoding.UTF8.GetBytes(credentials));
                byte[] Bytes = new byte[10485760];
                int ByteRec = Socket.Receive(Bytes);
                string Response = Encoding.UTF8.GetString(Bytes, 0, ByteRec).Trim();

                Console.WriteLine();

                if (Response.StartsWith("/error"))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    switch (Response)
                    {
                        case "/error Blocked":
                            Console.WriteLine("Вы в ЧЁРНОМ СПИСКЕ! Обратитесь к администратору.");
                            break;
                        case "/error Auth failed":
                        case "/error User not found":
                            Console.WriteLine("Неверный логин или пароль!");
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine("Подсказка: Для регистрации используйте формат:");
                            Console.WriteLine("      логин:пароль:register");
                            break;
                        case "/error Limit reached":
                            Console.WriteLine("Все лицензии заняты! Подождите или обратитесь к админу.");
                            break;
                        case "/error Already exists":
                            Console.WriteLine("Пользователь с таким логином уже существует!");
                            break;
                        default:
                            Console.WriteLine("Ошибка сервера: " + Response.Replace("/error ", ""));
                            break;
                    }
                }
                else if (Response.StartsWith("/success"))
                {
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.WriteLine("УСПЕШНО! " + Response.Replace("/success ", ""));
                    Console.WriteLine("Теперь вы можете войти с этим логином и паролем.");
                }
                else
                {
                    ClientToken = Response;
                    ClientDateConnection = DateTime.Now;
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("ЛИЦЕНЗИЯ ПОЛУЧЕНА!");
                    Console.WriteLine($"Токен: {ClientToken}");
                    Console.WriteLine($"Токен активен. Проверка каждую секунду...");
                }
            }
            catch (Exception exp)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Ошибка подключения: " + exp.Message);
            }
        }

        static void Help()
        {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("════════════════════════════════════════════════");
                Console.WriteLine("        LICENSE MANAGER            ");
                Console.WriteLine("════════════════════════════════════════════════");
                Console.WriteLine("/connect — login or register");
                Console.WriteLine("/status  — check token status");
                Console.WriteLine("/config  — change server IP/port");
                Console.WriteLine("/help    — show this menu");
                Console.WriteLine();

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("How to log in:");
                Console.WriteLine(" → Just enter your login and password");
                Console.WriteLine();

                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine("How to register a new account:");
                Console.WriteLine(" → Type: login:password:register");
                Console.WriteLine(" → Example: john:MySecurePass123:register");
                Console.WriteLine();

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("════════════════════════════════════════════════");
                Console.ResetColor();
            
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

        static void OnSettings()
        {
            string Path = Directory.GetCurrentDirectory() + "/.config";
            if (File.Exists(Path))
            {
                string[] lines = File.ReadAllLines(Path);
                ServerIpAddress = IPAddress.Parse(lines[0]);
                ServerPort = int.Parse(lines[1]);
                Console.ForegroundColor= ConsoleColor.DarkGreen;
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