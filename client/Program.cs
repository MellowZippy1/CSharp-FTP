using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Net.NetworkInformation;
using MessageNS;
using System;
using System.IO;


class Program
{
    static void Main(string[] args)
    {
        ClientUDP cUDP = new ClientUDP();
        cUDP.Start();
    }
}

class ClientUDP
{
    private const int ServerPort = 32000;
    private Socket client = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    private const int Threshold = 20;
    private int TxtCount = 0;

    private Dictionary<int, string>? packets = new Dictionary<int, string>();
    public void Start()
    {
        try
        {
            // Fill packet dict
            for (int i = 1; i <= 999; i++)
            {
                packets!.Add(i, "");
            }

            Console.WriteLine("Client started!");

            // Generate objects like host address and server endpoint
            (IPAddress hostAddress, IPEndPoint serverEndpoint) objects = GenerateObjects();

            //Console.WriteLine($"Expected host address: {objects.hostAddress}");
            //Console.WriteLine($"Expected server address: {objects.serverEndpoint}");

            // Create messages
            Message hello_message = new Message
            {
                Type = MessageType.Hello,
                Content = $"{Threshold}"
            };

            // Send the Hello message to the server
            SendMessage(hello_message, objects.serverEndpoint);

            // Receive message from server
            ReceiveMessage(objects.serverEndpoint);

            while (true)
            {
                ReceiveMessage(objects.serverEndpoint);
            }

        }
        catch (Exception ex)
        {
            HandleErrors(ex);
        }
        finally
        {
            client.Close();
            client.Dispose();
        }
    }

    private (IPAddress hostAddress, IPEndPoint serverEndPoint) GenerateObjects()
    {
        string ipOutput = "";

        foreach (NetworkInterface item in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (item.NetworkInterfaceType != NetworkInterfaceType.Loopback && item.OperationalStatus == OperationalStatus.Up)
            {
                foreach (UnicastIPAddressInformation ip in item.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        ipOutput = ip.Address.ToString();
                        break;
                    }
                }
            }
        }

        IPAddress hostAddress = IPAddress.Parse(ipOutput);

        if (hostAddress is null)
        {
            throw new Exception("Client host address is null");
        }

        IPEndPoint serverEndpoint = new IPEndPoint(hostAddress, ServerPort);

        return (hostAddress, serverEndpoint);
    }

    //private (IPAddress hostAddress, IPEndPoint serverEndPoint) GenerateObjects()
    //{
    //    IPAddress hostAddress = IPAddress.Loopback; // Returns loopback address 127.0.0.1
    //    IPEndPoint serverEndpoint = new IPEndPoint(hostAddress, ServerPort);

    //    return (hostAddress, serverEndpoint);
    //}

    private void SendMessage(Message message, IPEndPoint endPoint)
    {
        if (message is null) return;

        try
        {
            string serializedMessage = JsonSerializer.Serialize(message);
            byte[] data = Encoding.ASCII.GetBytes(serializedMessage);

            Console.WriteLine($"Sending {data.Length} bytes of type {message.Type} to {endPoint}");

            client.SendTo(data, endPoint);
        }
        catch (Exception ex)
        {
            HandleErrors(ex);
        }
    }

    private void ReceiveMessage(IPEndPoint endPoint)
    {
        try
        {
            EndPoint serverEndPoint = new IPEndPoint(IPAddress.Any, 0);

            byte[] buffer = new byte[4096]; // Adjust the buffer size according to your message size
            int bytesRead = client.ReceiveFrom(buffer, ref serverEndPoint);
            string receivedMessage = Encoding.ASCII.GetString(buffer).Trim('\0');

            // Deserialize the received message back into a Message object
            Message message = JsonSerializer.Deserialize<Message>(receivedMessage)!;
            //Console.WriteLine($"Message received with {message.Type}");

            // Handle message based on type
            switch (message.Type)
            {
                case MessageType.Welcome:
                    HandleWelcome(endPoint);
                    break;
                case MessageType.End:
                    HandleEnd();
                    break;
                case MessageType.Data:
                    HandleData(message, endPoint);
                    break;
                default:
                    HandleUnknown(message, endPoint);
                    break;
            }
        }
        catch (Exception ex)
        {
            HandleErrors(ex);
        }
    }

    private void HandleData(Message message, IPEndPoint endPoint)
    {
        Console.WriteLine($"Message received with {message.Type} and ID -> {message.Content![..4]}");
        //Console.WriteLine("Content: " + message.Content);
        //Console.WriteLine($"Set {Convert.ToInt32(message.Content![..4])} to {message.Content[..10]}");

        packets![Convert.ToInt32(message.Content![..4])] = message.Content[4..];

        SendMessage(new Message { Type = MessageType.Ack, Content = message.Content.Substring(0, 4) }, endPoint);
    }

    private void HandleWelcome(IPEndPoint endPoint)
    {
        Message requestdata = new Message
        {
            Type = MessageType.RequestData,
            Content = $"{Threshold}"
        };

        // Send the request data message to the server
        SendMessage(requestdata, endPoint);
    }

    private void HandleEnd()
    {
        string filePath = "output.txt";
        FileWriter fileWriter = new FileWriter();

        foreach (KeyValuePair<int, string> packet in packets!)
        {
            fileWriter.WriteToFile(filePath, packet.Value);
        }
        // Long timeout expired!
        Console.WriteLine("Packet sending done, 'END' received from server!\nShutting down client!");
        client.Close();
        client.Dispose();
        Environment.Exit(0);
    }

    private void HandleErrors(Exception ex)
    {
        Console.WriteLine($"Client error occurred: {ex.Message}.");
    }

    private void HandleUnknown(Message message, IPEndPoint endPoint)
    {
        Console.WriteLine("Handle unknown triggered!");
    }
    class FileWriter
    {
        public void WriteToFile(string filePath, string content)
        {
            try
            {
                // Replace occurrences of "\n" with the platform-specific newline sequence
                content = content.Replace("\n", Environment.NewLine);

                // Create a new file or append to it if it already exists
                using (StreamWriter writer = new StreamWriter(filePath, true))
                {
                    // Write the modified content to the file
                    writer.Write(content);
                }

                //Console.WriteLine("File write successful.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error writing to file: {ex.Message}");
            }
        }
    }
}