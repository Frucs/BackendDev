using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

const int Port = 5000;
const string UsersFile = "users.json";

TcpListener server = new(IPAddress.Any, Port);

ConcurrentDictionary<string, ConnectedClient> connectedClients =
    new(StringComparer.OrdinalIgnoreCase);

List<User> users = LoadUsers();

Console.WriteLine($"Server started on port {Port}.");
Console.WriteLine("Waiting for clients...");

server.Start();

while (true)
{
    TcpClient tcpClient = await server.AcceptTcpClientAsync();

    Console.WriteLine("Client connected.");

    _ = HandleClient(tcpClient);
}


// ============================================================
// CLIENT HANDLING
// ============================================================

async Task HandleClient(TcpClient tcpClient)
{
    NetworkStream stream = tcpClient.GetStream();

    ConnectedClient? connectedClient = null;

    try
    {
        ChatMessage? firstMessage = await ReadMessage(stream);

        if (firstMessage == null)
        {
            return;
        }

        // ----------------------------------------------------
        // REGISTER
        // ----------------------------------------------------

        if (firstMessage.Type == "register")
        {
            connectedClient = await HandleRegister(
                tcpClient,
                stream,
                firstMessage
            );
        }

        // ----------------------------------------------------
        // LOGIN
        // ----------------------------------------------------

        else if (firstMessage.Type == "login")
        {
            connectedClient = await HandleLogin(
                tcpClient,
                stream,
                firstMessage
            );
        }

        // ----------------------------------------------------
        // UNKNOWN
        // ----------------------------------------------------

        else
        {
            await SendRawMessage(
                stream,
                new ChatMessage
                {
                    Type = "error",
                    Content = "You must register or login first."
                }
            );

            return;
        }

        // Authentication failed
        if (connectedClient == null)
        {
            return;
        }

        // Add authenticated client
        connectedClients[connectedClient.Username] = connectedClient;

        Console.WriteLine(
            $"{connectedClient.Username} joined the chat."
        );

        // Tell everyone
        await Broadcast(
            new ChatMessage
            {
                Type = "system",
                Content = $"{connectedClient.Username} joined the chat."
            }
        );

        // Start chat
        await ChatLoop(
            stream,
            connectedClient
        );
    }
    catch (Exception ex)
    {
        Console.WriteLine(
            $"Client error: {ex.Message}"
        );
    }
    finally
    {
        // ----------------------------------------------------
        // DISCONNECT CLEANUP
        // ----------------------------------------------------

        if (connectedClient != null)
        {
            connectedClients.TryRemove(
                connectedClient.Username,
                out _
            );

            Console.WriteLine(
                $"{connectedClient.Username} left the chat."
            );

            try
            {
                await Broadcast(
                    new ChatMessage
                    {
                        Type = "system",
                        Content =
                            $"{connectedClient.Username} left the chat."
                    }
                );
            }
            catch
            {
                // Ignore errors while broadcasting disconnect.
            }

            connectedClient.SendLock.Dispose();
        }

        try
        {
            stream.Close();
            tcpClient.Close();
        }
        catch
        {
            // Ignore cleanup errors.
        }
    }
}


// ============================================================
// REGISTER
// ============================================================

async Task<ConnectedClient?> HandleRegister(
    TcpClient tcpClient,
    NetworkStream stream,
    ChatMessage message)
{
    string username = message.Username ?? "";
    string publicKey = message.PublicKey ?? "";

    // Validate username
    if (string.IsNullOrWhiteSpace(username))
    {
        await SendRawMessage(
            stream,
            new ChatMessage
            {
                Type = "error",
                Content = "Username cannot be empty."
            }
        );

        return null;
    }

    if (username.Length < 3 || username.Length > 20)
    {
        await SendRawMessage(
            stream,
            new ChatMessage
            {
                Type = "error",
                Content =
                    "Username must be between 3 and 20 characters."
            }
        );

        return null;
    }

    foreach (char c in username)
    {
        if (!char.IsLetterOrDigit(c) && c != '_')
        {
            await SendRawMessage(
                stream,
                new ChatMessage
                {
                    Type = "error",
                    Content =
                        "Username may only contain letters, numbers and underscore."
                }
            );

            return null;
        }
    }

    // Check username already exists
    if (users.Any(u =>
        string.Equals(
            u.Username,
            username,
            StringComparison.OrdinalIgnoreCase)))
    {
        await SendRawMessage(
            stream,
            new ChatMessage
            {
                Type = "error",
                Content = "That username is already taken."
            }
        );

        return null;
    }

    // Check public key
    if (string.IsNullOrWhiteSpace(publicKey))
    {
        await SendRawMessage(
            stream,
            new ChatMessage
            {
                Type = "error",
                Content = "Public key is missing."
            }
        );

        return null;
    }

    // Make sure the public key is actually valid RSA
    try
    {
        using RSA rsa = RSA.Create();

        rsa.ImportFromPem(publicKey);
    }
    catch
    {
        await SendRawMessage(
            stream,
            new ChatMessage
            {
                Type = "error",
                Content = "Invalid public key."
            }
        );

        return null;
    }

    // Create user
    User user = new()
    {
        Username = username,
        PublicKey = publicKey
    };

    users.Add(user);

    SaveUsers();

    Console.WriteLine(
        $"Registered new user: {username}"
    );

    // Tell client registration succeeded
    await SendRawMessage(
        stream,
        new ChatMessage
        {
            Type = "registered",
            Username = username,
            Content = "Registration successful."
        }
    );

    // The client is already authenticated after registration
    return new ConnectedClient(
        tcpClient,
        username
    );
}


// ============================================================
// LOGIN
// ============================================================

async Task<ConnectedClient?> HandleLogin(
    TcpClient tcpClient,
    NetworkStream stream,
    ChatMessage message)
{
    string username = message.Username ?? "";

    User? user = users.FirstOrDefault(u =>
        string.Equals(
            u.Username,
            username,
            StringComparison.OrdinalIgnoreCase));

    if (user == null)
    {
        await SendRawMessage(
            stream,
            new ChatMessage
            {
                Type = "error",
                Content = "User does not exist."
            }
        );

        return null;
    }

    // --------------------------------------------------------
    // Create challenge
    // --------------------------------------------------------

    byte[] challengeBytes =
        RandomNumberGenerator.GetBytes(32);

    string challenge =
        Convert.ToBase64String(challengeBytes);

    await SendRawMessage(
        stream,
        new ChatMessage
        {
            Type = "challenge",
            Challenge = challenge
        }
    );

    // --------------------------------------------------------
    // Wait for signed challenge
    // --------------------------------------------------------

    ChatMessage? response = await ReadMessage(stream);

    if (response == null ||
        response.Type != "challenge_response")
    {
        await SendRawMessage(
            stream,
            new ChatMessage
            {
                Type = "error",
                Content = "Invalid challenge response."
            }
        );

        return null;
    }

    string signatureText =
        response.Signature ?? "";

    if (string.IsNullOrWhiteSpace(signatureText))
    {
        await SendRawMessage(
            stream,
            new ChatMessage
            {
                Type = "error",
                Content = "Signature is missing."
            }
        );

        return null;
    }

    // --------------------------------------------------------
    // Verify signature
    // --------------------------------------------------------

    try
    {
        byte[] signature =
            Convert.FromBase64String(signatureText);

        byte[] challengeData =
            Convert.FromBase64String(challenge);

        using RSA rsa = RSA.Create();

        rsa.ImportFromPem(user.PublicKey);

        bool valid = rsa.VerifyData(
            challengeData,
            signature,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );

        if (!valid)
        {
            await SendRawMessage(
                stream,
                new ChatMessage
                {
                    Type = "error",
                    Content = "Authentication failed."
                }
            );

            return null;
        }
    }
    catch
    {
        await SendRawMessage(
            stream,
            new ChatMessage
            {
                Type = "error",
                Content = "Invalid authentication data."
            }
        );

        return null;
    }

    // --------------------------------------------------------
    // Login successful
    // --------------------------------------------------------

    await SendRawMessage(
        stream,
        new ChatMessage
        {
            Type = "login_success",
            Username = user.Username,
            Content = "Login successful."
        }
    );

    Console.WriteLine(
        $"User authenticated: {user.Username}"
    );

    return new ConnectedClient(
        tcpClient,
        user.Username
    );
}


// ============================================================
// CHAT LOOP
// ============================================================

async Task ChatLoop(
    NetworkStream stream,
    ConnectedClient connectedClient)
{
    while (true)
    {
        ChatMessage? message =
            await ReadMessage(stream);

        // Client disconnected
        if (message == null)
        {
            break;
        }

        // We only accept chat messages here
        if (message.Type != "message")
        {
            continue;
        }

        string content =
            message.Content ?? "";

        if (string.IsNullOrWhiteSpace(content))
        {
            continue;
        }

        // ----------------------------------------------------
        // QUIT
        // ----------------------------------------------------

        if (content == "/quit")
        {
            break;
        }

        // ----------------------------------------------------
        // Broadcast chat message
        // ----------------------------------------------------

        await Broadcast(
            new ChatMessage
            {
                Type = "chat",
                Username = connectedClient.Username,
                Content = content
            }
        );
    }
}


// ============================================================
// SEND RAW MESSAGE
//
// Used before a client has become a ConnectedClient.
// ============================================================

async Task SendRawMessage(
    NetworkStream stream,
    ChatMessage message)
{
    string json =
        JsonSerializer.Serialize(message) + "\n";

    byte[] data =
        Encoding.UTF8.GetBytes(json);

    await stream.WriteAsync(data);
}


// ============================================================
// SEND MESSAGE
//
// Used for authenticated ConnectedClients.
// SendLock prevents two broadcasts from writing
// simultaneously to the same TCP connection.
// ============================================================

async Task SendMessage(
    ConnectedClient client,
    ChatMessage message)
{
    string json =
        JsonSerializer.Serialize(message) + "\n";

    byte[] data =
        Encoding.UTF8.GetBytes(json);

    await client.SendLock.WaitAsync();

    try
    {
        await client.Stream.WriteAsync(data);
    }
    finally
    {
        client.SendLock.Release();
    }
}


// ============================================================
// BROADCAST
// ============================================================

async Task Broadcast(ChatMessage message)
{
    List<Task> tasks = new();

    foreach (ConnectedClient client in connectedClients.Values)
    {
        tasks.Add(
            SendMessage(client, message)
        );
    }

    await Task.WhenAll(tasks);
}


// ============================================================
// READ MESSAGE
//
// Messages are separated by \n.
// ============================================================

async Task<ChatMessage?> ReadMessage(
    NetworkStream stream)
{
    using MemoryStream memory = new();

    byte[] buffer = new byte[1];

    while (true)
    {
        int bytesRead =
            await stream.ReadAsync(buffer);

        // Client disconnected
        if (bytesRead == 0)
        {
            return null;
        }

        // End of JSON message
        if (buffer[0] == '\n')
        {
            break;
        }

        memory.WriteByte(buffer[0]);

        // Safety limit
        if (memory.Length > 100_000)
        {
            throw new Exception(
                "Message too large."
            );
        }
    }

    string json =
        Encoding.UTF8.GetString(
            memory.ToArray()
        );

    return JsonSerializer.Deserialize<ChatMessage>(
        json
    );
}


// ============================================================
// USERS
// ============================================================

List<User> LoadUsers()
{
    if (!File.Exists(UsersFile))
    {
        return new List<User>();
    }

    try
    {
        string json =
            File.ReadAllText(UsersFile);

        return JsonSerializer.Deserialize<List<User>>(json)
            ?? new List<User>();
    }
    catch
    {
        Console.WriteLine(
            "Could not read users.json. Starting with no users."
        );

        return new List<User>();
    }
}


void SaveUsers()
{
    string json =
        JsonSerializer.Serialize(
            users,
            new JsonSerializerOptions
            {
                WriteIndented = true
            }
        );

    File.WriteAllText(
        UsersFile,
        json
    );
}


// ============================================================
// DATA CLASSES
// ============================================================

class User
{
    public string Username { get; set; } = "";

    public string PublicKey { get; set; } = "";
}


class ConnectedClient
{
    public TcpClient TcpClient { get; }

    public NetworkStream Stream { get; }

    public string Username { get; }

    public SemaphoreSlim SendLock { get; } =
        new SemaphoreSlim(1, 1);

    public ConnectedClient(
        TcpClient tcpClient,
        string username)
    {
        TcpClient = tcpClient;
        Stream = tcpClient.GetStream();
        Username = username;
    }
}


class ChatMessage
{
    public string Type { get; set; } = "";

    public string? Username { get; set; }

    public string? PublicKey { get; set; }

    public string? Challenge { get; set; }

    public string? Signature { get; set; }

    public string? Content { get; set; }
}