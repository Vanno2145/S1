using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        var port = 5000;
        var ipAddress = "127.0.0.1";

        using var tcpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        try
        {
            
            await tcpSocket.ConnectAsync(ipAddress, port);
            Console.WriteLine($"Подключение к серверу {ipAddress}:{port} установлено");

          
            Console.WriteLine("Введите сообщение для отправки на сервер:");
            var message = Console.ReadLine();
            var messageBytes = Encoding.UTF8.GetBytes(message);
            await tcpSocket.SendAsync(messageBytes, SocketFlags.None);
            Console.WriteLine($"Сообщение отправлено: {message}");

           
            var buffer = new byte[1024];
            int bytesReceived = await tcpSocket.ReceiveAsync(buffer, SocketFlags.None);
            string response = Encoding.UTF8.GetString(buffer, 0, bytesReceived);
            Console.WriteLine($"Ответ от сервера: {response}");

            tcpSocket.Shutdown(SocketShutdown.Both);
            tcpSocket.Close();
        }
        catch (SocketException)
        {
            Console.WriteLine("Не удалось подключиться к серверу");
        }
    }
}
