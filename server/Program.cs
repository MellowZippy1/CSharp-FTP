using System;
using System.Net.NetworkInformation;
using System.Data.SqlTypes;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using MessageNS;


// Do not modify this class
class Program
{
    static void Main(string[] args)
    {
        ServerUDP sUDP = new ServerUDP();
        sUDP.start();
    }
}

class ServerUDP
{
    // Initialization of Server socket.
    private Socket Server = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

    // Tuple that holds information about what packets have been exchanged between the client and server
    private (bool helloReceived, bool welcomeSent, bool RequestDataReceived, bool EndSent) SequenceHolder = new(false, false, false, false);

    // Check if an error has been sent.
    private bool ErrorSent = false;

    // Port that Server is bound to.
    private const int Port = 32000;

    // Amount of bytes that will be in a packet's content.
    private const int PayloadSize = 1024;

    // Packets that will be sent to client
    // Format: Id, (isSent, Message)
    private Dictionary<int, (bool, Message)> Packets = null!;

    // Packet threshold of client
    private int ClientThreshold = 0;

    // Current speed of packet traffic
    private int CurrentSpeed = 1;

    //TODO: implement all necessary logic to create sockets and handle incoming messages
    // Do not put all the logic into one method. Create multiple methods to handle different tasks.
    public void start()
    {
        InitializeServer();

        try
        {
            Console.WriteLine($"Server has started listening on Port {Port}");
            while (true)
            {
                if (SequenceHolder.EndSent || ErrorSent)
                {
                    ResetServer();
                    start();
                }

                // Start receiving messages
                var buffer = new byte[1024];
                EndPoint clientEndpoint = new IPEndPoint(IPAddress.Any, 0);
                Server.ReceiveFrom(buffer, ref clientEndpoint);

                // Once a client connects, handle the message
                HandleClientMessage(buffer, clientEndpoint);
            }
        }
        catch (Exception ex)
        {
            // Log error
            Console.WriteLine("An error occured. Restarting server.\n{0}", ex.ToString());

            // Restart server
            ResetServer();
            start();
        }
    }

    //TODO: create all needed objects for your sockets
    private void InitializeServer()
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

        // Attempt binding server to port
        try
        {
            var serverEndpoint = new IPEndPoint(hostAddress, Port);
            Server.Bind(serverEndpoint);
            Console.WriteLine("Server Socket bound to {0}", serverEndpoint);
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed to connect to IPv4 address on port {Port}\n{e.ToString}");
        }

        Console.WriteLine("Server initialized.");
    }

    private void InitializePackets(string FilePath)
    {
        var packets = new Dictionary<int, (bool, Message)>();

        // Check if data file exists.
        if (!File.Exists(FilePath))
        {
            throw new FileNotFoundException("Data file not found.", FilePath);
        }

        Console.WriteLine($"Preparing packets for {FilePath}");

        using (StreamReader reader = new StreamReader(FilePath))
        {
            int packetNumber = 0;
            int bytesRead;
            var buffer = new char[PayloadSize];

            while ((bytesRead = reader.ReadBlock(buffer, 0, PayloadSize)) > 0)
            {
                packetNumber += 1;

                packets[packetNumber] = (false, new Message { Type = MessageType.Data, Content = GetUniqueId(packetNumber) + new string(buffer, 0, bytesRead) });
            }
        }

        Packets = packets;
        Console.WriteLine("Packets initialized.");
    }

    private string GetUniqueId(int packet_number) => packet_number <= 0 ? null! : packet_number.ToString().PadLeft(4, '0');

    private void SendMessage(Message message, EndPoint clientEndpoint)
    {
        if (message is null) return;

        if (message.Type == MessageType.Error)
        {
            ErrorSent = true;
        }

        try
        {
            // Convert Message object to JSON
            var serializedMessage = JsonSerializer.Serialize(message);

            // Convert JSON to byte[]
            var encodedMessage = Encoding.ASCII.GetBytes(serializedMessage);

            // Send encoded message to endpoint
            int bytesSent = Server.SendTo(encodedMessage, clientEndpoint);

            // Log message
            Console.WriteLine($"Sending {bytesSent} bytes of type {message.Type} to {clientEndpoint}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unable to send message to {clientEndpoint}\n{ex.ToString}");
        }
    }

    private void SendMessage(MessageType messageType, EndPoint clientEndpoint)
    {
        var message = new Message { Type = messageType };
        SendMessage(message, clientEndpoint);
    }

    private void HandleClientMessage(byte[] buffer, EndPoint clientEndpoint)
    {
        if (buffer is null || buffer.Length == 0)
        {
            return;
        }

        // Convert byte[] to string
        var decodedMessage = Encoding.ASCII.GetString(buffer).Trim('\0');

        // Convert string to a Message object
        var receivedMessage = JsonSerializer.Deserialize<Message>(decodedMessage);

        if (receivedMessage is null)
        {
            Console.WriteLine("Failed to deserialize the message.");
            return;
        }

        // Log receiving a message
        Console.WriteLine($"Received {decodedMessage.Length} bytes of type {receivedMessage.Type} ({receivedMessage.Content})");

        // Handle message based on type
        switch (receivedMessage.Type)
        {
            case MessageType.Hello:
                HandleHello(receivedMessage, clientEndpoint);
                break;
            case MessageType.RequestData:
                HandleRequestData(receivedMessage, clientEndpoint);
                break;
            case MessageType.Ack:
                HandleAck(receivedMessage, clientEndpoint);
                break;
            default:
                // Send error to client due to invalid message type.
                SendMessage(MessageType.Error, clientEndpoint);
                break;
        }
    }

    private void HandleAck(Message message, EndPoint clientEndpoint)
    {
        if (message.Type != MessageType.Ack)
        {
            throw new Exception("Non-Ack message handled.");
        }

        bool idFound = int.TryParse(message.Content![..4], out int packetId);

        if (idFound)
        {
            // Acknowledge packet
            var existingPacket = Packets[packetId];
            Packets[packetId] = (true, existingPacket.Item2);
            Console.WriteLine($"Acknowledged packet {packetId}");
        }
        else
        {
            // Send error to client, unable to recognize packet
            SendMessage(MessageType.Error, clientEndpoint);
            Console.WriteLine("Unable to recognize packet.");
        }
    }

    private void HandleHello(Message message, EndPoint clientEndpoint)
    {
        if (message.Type != MessageType.Hello)
        {
            throw new Exception("Non-hello message handled.");
        }

        // If exchange is already happening then deny
        if (SequenceHolder.helloReceived)
        {
            // Send error back to client
            SendMessage(MessageType.Error, clientEndpoint);
            return;
        }

        SequenceHolder = (true, false, false, false);

        // Decode threshold
        bool foundThreshold = int.TryParse(message.Content, out int threshold);
        if (foundThreshold)
        {
            // Check for invalid threshold
            if (threshold <= 0)
            {
                SendMessage(MessageType.Error, clientEndpoint);
                return;
            }

            Console.WriteLine("Threshold of {0} found", threshold);
            ClientThreshold = threshold;
        }
        else
        {
            Console.WriteLine("Threshold set to default (20)");
            ClientThreshold = 20;
        }

        // Send welcome to client
        SendMessage(MessageType.Welcome, clientEndpoint);
        SequenceHolder = (true, true, false, false);

        // Set timeout due to communication being established.
        Server.ReceiveTimeout = 5000;
    }

    //TODO: [Receive RequestData]
    //TODO: [Send Data]
    //TODO: [Implement your slow-start algorithm considering the threshold] 
    private void HandleRequestData(Message message, EndPoint clientEndpoint)
    {
        if (message.Type != MessageType.RequestData)
        {
            throw new Exception("Non-RequestData message handled.");
        }

        // Check for improper sequence
        if (!SequenceHolder.helloReceived || !SequenceHolder.welcomeSent || SequenceHolder.RequestDataReceived)
        {
            SendMessage(MessageType.Error, clientEndpoint);
            return;
        }

        if (message.Content is null || !File.Exists(message.Content))
        {
            SendMessage(MessageType.Error, clientEndpoint);
        }

        InitializePackets(message.Content!);

        // Check if there are packets to send.
        if (Packets is null)
        {
            throw new Exception("No packets available.");
        }

        SequenceHolder = (true, true, true, false);

        SendDataReliably(clientEndpoint);
        SendMessage(MessageType.End, clientEndpoint);
        SequenceHolder = (true, true, true, true);
    }

    private void SendDataReliably(EndPoint clientEndPoint)
    {

        while (!AllPacketsSent())
        {
            // Prepare messages
            var sendingPackets = FindFirstAvailablePacket();

            // Send messages
            foreach (var kvp in sendingPackets)
            {

                var packetId = kvp.Item1;
                var packet = kvp.Item2;

                if (packetId == 0)
                {
                    break;
                }

                // Send packet data
                SendMessage(packet, clientEndPoint);
                Console.WriteLine($"Sent packet {packetId}");
            }

            foreach (var kvp in sendingPackets)
            {
                var packetId = kvp.Item1;
                WaitForAcknowledgment(clientEndPoint, packetId);
            }

            if (AllPacketsSent(sendingPackets[sendingPackets.Length - 1].Item1))
            {
                CurrentSpeed = Math.Min(CurrentSpeed * 2, ClientThreshold);
            }
            else
            {
                CurrentSpeed = 1;
            }
        }
    }

    private (int, Message)[] FindFirstAvailablePacket()
    {
        var messages = new (int, Message)[CurrentSpeed];

        int count = 0;
        foreach (var kvp in Packets)
        {
            if (count == messages.Length)
            {
                break;
            }

            if (kvp.Value.Item1 == false)
            {
                messages[count++] = (kvp.Key, kvp.Value.Item2);
            }
        }

        return messages;
    }

    private void WaitForAcknowledgment(EndPoint clientEndPoint, int messageId)
    {
        byte[] buffer = new byte[PayloadSize];

        // Set timeout for acknowledgment
        Server.ReceiveTimeout = 1000;

        try
        {
            int receivedBytes = Server.Receive(buffer);
            var receivedMessage = JsonSerializer.Deserialize<Message>(Encoding.ASCII.GetString(buffer, 0, receivedBytes));

            // Check if received acknowledgment matches messageId
            if (receivedMessage?.Type == MessageType.Ack && int.TryParse(receivedMessage.Content, out int ackId) && ackId == messageId)
            {
                Console.WriteLine($"Received acknowledgment for message {ackId}");
                Packets[messageId] = (true, Packets[messageId].Item2);
            }
            else
            {
                // If acknowledgment is invalid, reset speed
                Console.WriteLine("Invalid acknowledgment received.");
                CurrentSpeed = 1;

                // SendMessage(Packets[messageId].Item2, clientEndPoint);
            }
        }
        catch
        {
            // Handle timeout or other socket errors
            Console.WriteLine($"Did not receive acknowledgment of packet {messageId} in time.");
            // Handle error or retransmission
            // For simplicity, retransmitting the packet
            // SendMessage(MessageType.Error, clientEndPoint);
        }

        Server.ReceiveTimeout = 5000;
    }

    private bool CheckMessages(int[] messages)
    {
        foreach (var message in messages)
        {
            if (Packets[message].Item1 == false)
            {
                return false;
            }
        }

        return true;
    }

    private bool AllPacketsSent()
    {
        foreach (var kvp in Packets)
        {
            if (!kvp.Value.Item1)
            {
                return false;
            }
        }

        return true;
    }

    private bool AllPacketsSent(int max)
    {
        int count = 0;

        foreach (var kvp in Packets)
        {
            if (count == max)
            {
                break;
            }

            if (!kvp.Value.Item1)
            {
                return false;
            }

            count++;
        }

        return true;
    }

    private void ResetServer()
    {
        Packets = null!;
        SequenceHolder = (false, false, false, false);
        ClientThreshold = 0;
        CurrentSpeed = 1;
        Server.ReceiveTimeout = 0;
        ErrorSent = false;
    }
}