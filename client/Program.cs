using System.ComponentModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using MessageNS;

// SendTo();
class Program
{
    static void Main(string[] args)
    {
        ClientUDP cUDP = new ClientUDP();
        cUDP.start();
    }
}

class ClientUDP
{
    // Initialize Client socket
    private Socket Client = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    
    // Port of the server
    private const int Port = 32000;

    // Traffic threshold of the client
    private const int Threshold = 3;

    // Output file for all received text
    private const string OutputFile = "output.txt";
    
    // Temporary storage for all incoming messages
    // Format: ID, Content
    private Dictionary<int, string> receivedText = new Dictionary<int, string>();

    // Tuple to hold track of the sequence between the server and the client
    private (bool HelloSent, bool WelcomeReceived, bool RequestDataSent, bool EndReceived) SequenceHolder = new();

    // Boolean variable to check if an error has been sent by the client
    private bool errorSent = false;

    // Boolean variable to check if an error has been received by the client
    private bool errorReceived = false;

    //TODO: implement all necessary logic to create sockets and handle incoming messages
    // Do not put all the logic into one method. Create multiple methods to handle different tasks.
    public void start()
    {
        (IPAddress hostAddress, IPEndPoint serverEndpoint) objects = InitializeClient();
        IPAddress hostAddress = objects.hostAddress;
        IPEndPoint serverEndpoint = objects.serverEndpoint;
        Console.WriteLine($"Host address: {hostAddress}\nExpected server address: {serverEndpoint}");

        try
        {
            // Create discovery message
            Message discovery = new Message
            {
                Type = MessageType.Hello,
                Content = $"{Threshold}"
            };

            // Send discovery message to the server
            SendMessage(discovery, serverEndpoint);
            SequenceHolder = (true, SequenceHolder.WelcomeReceived, SequenceHolder.RequestDataSent, SequenceHolder.EndReceived);

            // Start listening
            while (SequenceHolder.EndReceived == false && !errorReceived && !errorSent)
            {
                ReceiveMessage(serverEndpoint);
            }

            Console.WriteLine("Shutting down client.");
            Client.Close();
            Client.Dispose();
            Environment.Exit(0);
        }
        catch (Exception ex) 
        {
            Console.WriteLine(ex.ToString());
        }
    }

    //TODO: create all needed objects for your sockets 
    private (IPAddress hostAddress, IPEndPoint serverEndpoint) InitializeClient()
    {
        string ipOutput = "";

        try
        {
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
        }
        catch
        {
            Console.WriteLine("Unable to grab IP-address of this machine.");
        }

        IPAddress hostAddress = IPAddress.Parse(ipOutput) ?? throw new Exception("Client host address is null");
        IPEndPoint serverEndpoint = new IPEndPoint(hostAddress, Port);

        return (hostAddress, serverEndpoint);
    }

    //TODO: [Send Hello message]
    //TODO: [Send RequestData]
    //TODO: [Send RequestData]
    //TODO: [Send End] ??
    private void SendMessage(Message message, IPEndPoint serverEndpoint)
    {
        if (message is null) return;

        try
        {
            // Convert Message object to JSON
            var serializedMessage = JsonSerializer.Serialize(message);

            // Convert JSON to byte[]
            var encodedMessage = Encoding.ASCII.GetBytes(serializedMessage);

            // Log sending message
            Console.WriteLine($"Sending {encodedMessage.Length} bytes of type {message.Type} to {serverEndpoint}");

            // Send message
            Client.SendTo(encodedMessage, serverEndpoint);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occured while sending a message of type {message.Type} to {serverEndpoint}\n{ex.ToString}");
        }
    }

    private void SendMessage(MessageType messageType, IPEndPoint serverEndpoint)
    {
        Message message = new Message { Type = messageType };
        SendMessage(message, serverEndpoint);
    }

    //TODO: [Receive Welcome]
    //TODO: [Receive Data]
    //TODO: [Handle Errors]
    private void ReceiveMessage(IPEndPoint serverEndpoint)
    {
        try
        {
            EndPoint serverEndPoint = new IPEndPoint(IPAddress.Any, 0);

            byte[] buffer = new byte[4096]; // Adjust the buffer size according to your message size
            int bytesRead = Client.ReceiveFrom(buffer, ref serverEndPoint);
            
            // Decode message from byte[] to string
            string receivedMessage = Encoding.ASCII.GetString(buffer).Trim('\0');

            // Deserialize the received message back into a Message object
            Message message = JsonSerializer.Deserialize<Message>(receivedMessage)!;

            // Handle message based on type
            switch (message.Type)
            {
                case MessageType.Welcome:
                    HandleWelcome(serverEndpoint);
                    break;
                case MessageType.End:
                    HandleEnd();
                    break;
                case MessageType.Data:
                    HandleData(message, serverEndpoint);
                    break;
                default:
                    HandleUnknown(message, serverEndpoint);
                    break;
            }
        }
        catch
        {

        }
    }

    private void HandleUnknown(Message message, IPEndPoint serverEndpoint)
    {
        Console.WriteLine("Handle unknown triggered!");
        SendMessage(MessageType.Error, serverEndpoint);
        errorSent = true;
    }

    private void HandleData(Message message, IPEndPoint endPoint)
    {
        if (message is null) return;
        if (message.Content is null || message.Content.Length < 4) return;

        Console.WriteLine($"Message received with {message.Type} and ID -> {message.Content![..4]}");

        receivedText![Convert.ToInt32(message.Content![..4])] = message.Content[4..];

        SendMessage(new Message { Type = MessageType.Ack, Content = message.Content[..4] }, endPoint);
    }

    private void HandleEnd()
    {
        // Check for improper sequence
        if (!SequenceHolder.HelloSent || !SequenceHolder.WelcomeReceived || !SequenceHolder.RequestDataSent || SequenceHolder.EndReceived)
        {
            Console.WriteLine("Improper END packet received.");
            return;
        }

        SequenceHolder = (SequenceHolder.HelloSent, SequenceHolder.WelcomeReceived, SequenceHolder.RequestDataSent, true);
        Console.WriteLine("Packet sending done, 'END' received from server!");
        
        
        // Check if the file exists
        if (!File.Exists(OutputFile))
        {
            // Create the file if it doesn't exist
            File.Create(OutputFile).Close();
        }

        // Write all text to the output file
        using (StreamWriter sw = File.AppendText(OutputFile))
        {
            foreach (var kvp in receivedText)
            {
                sw.Write(kvp.Value);
            }
        }

        Console.WriteLine("Wrote all received packets to the output file! ({0})", OutputFile);
    }

    private void HandleWelcome(IPEndPoint serverEndpoint)
    {
        // Check for improper sequence
        if (!SequenceHolder.HelloSent || SequenceHolder.WelcomeReceived)
        {
            SendMessage(MessageType.Error, serverEndpoint);
            errorSent = true;
            return;
        }

        Message requestdata = new Message
        {
            Type = MessageType.RequestData,
            Content = $"{Threshold}"
        };

        SequenceHolder = (SequenceHolder.HelloSent, true, true, SequenceHolder.EndReceived);
        // Send the request data message to the server
        SendMessage(requestdata, serverEndpoint);
    }

}