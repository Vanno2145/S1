using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

class Program
{
    
    private static readonly int MaxRequests = 5;
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(1);

   
    private static readonly ConcurrentDictionary<string, (int requestCount, DateTime lastRequestTime)> clientData = new();

    static async Task Main(string[] args)
    {
        var port = 5000;
        var ipAddress = "127.0.0.1";

        using var listener = new TcpListener(IPAddress.Parse(ipAddress), port);
        listener.Start();
        Console.WriteLine($"Сервер слушает на {ipAddress}:{port}");

        while (true)
        {
          
            var client = await listener.AcceptTcpClientAsync();
            Console.WriteLine("Клиент подключён");

          
            var stream = client.GetStream();
            var clientEndPoint = client.Client.RemoteEndPoint.ToString();

        
            if (IsClientBlocked(clientEndPoint))
            {
                string blockedMessage = "Превышено количество запросов. Попробуйте подключиться через 1 минуту.";
                var blockedMessageBytes = Encoding.UTF8.GetBytes(blockedMessage);
                await stream.WriteAsync(blockedMessageBytes, 0, blockedMessageBytes.Length);
                Console.WriteLine($"Блокировка клиента {clientEndPoint}");
                client.Close();
                continue;
            }

      
            var buffer = new byte[1024];
            int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
            string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            Console.WriteLine($"Получено сообщение от клиента: {message}");

     
            var responseMessage = $"Сервер получил сообщение: {message}";
            var responseBytes = Encoding.UTF8.GetBytes(responseMessage);
            await stream.WriteAsync(responseBytes, 0, responseBytes.Length);

           
            UpdateClientData(clientEndPoint);

            client.Close();
        }
    }

    
    private static bool IsClientBlocked(string clientEndPoint)
    {
        if (clientData.TryGetValue(clientEndPoint, out var data))
        {
            if (data.requestCount >= MaxRequests && DateTime.Now - data.lastRequestTime < Timeout)
            {
                return true; 
            }
        }
        return false;
    }

  
    private static void UpdateClientData(string clientEndPoint)
    {
        if (clientData.ContainsKey(clientEndPoint))
        {
            var data = clientData[clientEndPoint];
         
            if (DateTime.Now - data.lastRequestTime > Timeout)
            {
                clientData[clientEndPoint] = (1, DateTime.Now); 
            }
            else
            {
                clientData[clientEndPoint] = (data.requestCount + 1, DateTime.Now);
            }
        }
        else
        {
            clientData[clientEndPoint] = (1, DateTime.Now);
        }
    }
}
