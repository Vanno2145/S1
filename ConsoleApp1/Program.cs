using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace SimpleChat
{
    class Program
    {
        static bool isServer = false;
        static string username = "";
        static List<TcpClient> clients = new List<TcpClient>();
        static TcpListener server;

        static void Main(string[] args)
        {
            Console.WriteLine("Запустить как (1 - Сервер, 2 - Клиент):");
            var choice = Console.ReadLine();

            if (choice == "1")
            {
                StartServer();
            }
            else
            {
                StartClient();
            }
        }

        static void StartServer()
        {
            isServer = true;
            Console.Write("Введите ваше имя: ");
            username = Console.ReadLine();

            server = new TcpListener(IPAddress.Any, 8888);
            server.Start();
            Console.WriteLine("Сервер запущен. Ожидание подключений...");

            new Thread(() =>
            {
                while (true)
                {
                    var client = server.AcceptTcpClient();
                    clients.Add(client);
                    Console.WriteLine("Новое подключение!");
                    Thread clientThread = new Thread(() => HandleClient(client));
                    clientThread.Start();
                }
            }).Start();

            SendMessages();
        }

        static void StartClient()
        {
            Console.Write("Введите ваше имя: ");
            username = Console.ReadLine();

            Console.Write("Введите IP сервера: ");
            var ip = Console.ReadLine();

            var client = new TcpClient();
            client.Connect(ip, 8888);
            clients.Add(client);

            Console.WriteLine("Подключено к серверу!");

            Thread receiveThread = new Thread(() => ReceiveMessages(client));
            receiveThread.Start();

            SendMessages(client);
        }

        static void HandleClient(TcpClient client)
        {
            ReceiveMessages(client);
        }

        static void ReceiveMessages(TcpClient client)
        {
            try
            {
                while (true)
                {
                    var stream = client.GetStream();
                    byte[] data = new byte[256];
                    int bytes = stream.Read(data, 0, data.Length);
                    string message = Encoding.UTF8.GetString(data, 0, bytes);
                    Console.WriteLine(message);
                }
            }
            catch
            {
                clients.Remove(client);
                if (isServer)
                    Console.WriteLine("Клиент отключился");
            }
        }

        static void SendMessages(TcpClient client = null)
        {
            while (true)
            {
                var message = Console.ReadLine();
                var fullMessage = $"[{username}]: {message}";

                if (isServer)
                {
                    // Сервер рассылает всем клиентам
                    foreach (var c in clients)
                    {
                        var stream = c.GetStream();
                        byte[] data = Encoding.UTF8.GetBytes(fullMessage);
                        stream.Write(data, 0, data.Length);
                    }
                }
                else
                {
                    // Клиент отправляет только серверу
                    var stream = client.GetStream();
                    byte[] data = Encoding.UTF8.GetBytes(fullMessage);
                    stream.Write(data, 0, data.Length);
                }
            }
        }
    }
}