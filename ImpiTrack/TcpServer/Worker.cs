using System.Net.Sockets;
using System.Net;
using System.Text;

namespace TcpServer
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;

        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Servidor TCP iniciado...");
            var listener = new TcpListener(IPAddress.Any, 5001);
            listener.Start();

            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Esperando conexiones...");
                var client = await listener.AcceptTcpClientAsync();
                _ = HandleClientAsync(client, stoppingToken);
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken token)
        {
            _logger.LogInformation("Cliente conectado.");
            var stream = client.GetStream();
            var buffer = new byte[4096];

            try
            {
                while (!token.IsCancellationRequested)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token);
                    if (bytesRead == 0) break;

                    string message = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                    _logger.LogInformation($"Mensaje recibido: {message}");

                    // Procesar el mensaje según el tipo
                    if (message.StartsWith("##"))  // Login
                    {
                        await File.AppendAllTextAsync("login_log.txt", $"{DateTime.Now}: {message}{Environment.NewLine}", token);
                        _logger.LogInformation("Login recibido. Respondiendo con 'LOAD'");
                        byte[] response = Encoding.ASCII.GetBytes("LOAD");
                        await stream.WriteAsync(response, 0, response.Length, token);
                    }
                    else if (message.Contains("imei:") && message.Contains(",tracker,"))  // Mensaje de ubicación
                    {
                        await File.AppendAllTextAsync("location_log.txt", $"{DateTime.Now}: {message}{Environment.NewLine}", token);
                        _logger.LogInformation("Mensaje de ubicación recibido. Respondiendo con 'ON'");
                        byte[] response = Encoding.ASCII.GetBytes("ON\r\n");
                        await stream.WriteAsync(response, 0, response.Length, token);
                    }
                    else
                    {
                        _logger.LogInformation("Mensaje no reconocido.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en la conexión.");
            }
            finally
            {
                _logger.LogInformation("Cliente desconectado.");
                client.Close();
            }
        }
    }
}