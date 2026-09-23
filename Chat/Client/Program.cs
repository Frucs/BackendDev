#pragma warning disable OPENAI001

using Spectre.Console;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

const string ServerIp = "172.24.91.53";
const int ServerPort = 5000;

const string IdentityFolder = "identity";
const string UsernameFile = "username.txt";
const string PrivateKeyFile = "private_key.pem";


// ============================================================
// START
// ============================================================

Directory.CreateDirectory(IdentityFolder);

string username;

RSA rsa;


// ============================================================
// LOAD EXISTING IDENTITY
// ============================================================

if (File.Exists(GetUsernamePath()) &&
    File.Exists(GetPrivateKeyPath()))
{
    username =
        File.ReadAllText(
            GetUsernamePath()
        ).Trim();

    string privateKey =
        File.ReadAllText(
            GetPrivateKeyPath()
        );

    rsa = RSA.Create();

    rsa.ImportFromPem(privateKey);

    AnsiConsole.MarkupLine(
        $"[green]Welcome back, {Markup.Escape(username)}![/]"
    );
}
else
{
    // New user

    username = AskForUsername();

    rsa = RSA.Create(2048);
}


// ============================================================
// CONNECT
// ============================================================

using TcpClient client = new();

AnsiConsole.MarkupLine(
    "[grey]Connecting to server...[/]"
);

await client.ConnectAsync(
    ServerIp,
    ServerPort
);

AnsiConsole.MarkupLine(
    "[green]Connected![/]"
);

NetworkStream stream =
    client.GetStream();


// ============================================================
// REGISTER OR LOGIN
// ============================================================

bool existingIdentity =
    File.Exists(GetUsernamePath()) &&
    File.Exists(GetPrivateKeyPath());


if (!existingIdentity)
{
    // --------------------------------------------------------
    // REGISTER
    // --------------------------------------------------------

    string publicKey =
        rsa.ExportSubjectPublicKeyInfoPem();

    await SendMessage(
        stream,
        new ChatMessage
        {
            Type = "register",
            Username = username,
            PublicKey = publicKey
        }
    );


    ChatMessage? response =
        await ReadMessage(stream);


    if (response == null)
    {
        AnsiConsole.MarkupLine(
            "[red]Server disconnected.[/]"
        );

        return;
    }


    if (response.Type == "error")
    {
        AnsiConsole.MarkupLine(
            $"[red]{Markup.Escape(response.Content ?? "Unknown error")}[/]"
        );

        return;
    }


    if (response.Type != "registered")
    {
        AnsiConsole.MarkupLine(
            "[red]Unexpected server response.[/]"
        );

        return;
    }


    // Registration succeeded.
    // NOW save the identity.

    string privateKey =
        rsa.ExportPkcs8PrivateKeyPem();

    File.WriteAllText(
        GetPrivateKeyPath(),
        privateKey
    );

    File.WriteAllText(
        GetUsernamePath(),
        username
    );


    AnsiConsole.MarkupLine(
        "[green]Account created successfully![/]"
    );

    AnsiConsole.MarkupLine(
        "[grey]Your private key has been saved locally.[/]"
    );
}
else
{
    // --------------------------------------------------------
    // LOGIN
    // --------------------------------------------------------

    await SendMessage(
        stream,
        new ChatMessage
        {
            Type = "login",
            Username = username
        }
    );


    ChatMessage? challengeMessage =
        await ReadMessage(stream);


    if (challengeMessage == null)
    {
        AnsiConsole.MarkupLine(
            "[red]Server disconnected.[/]"
        );

        return;
    }


    if (challengeMessage.Type == "error")
    {
        AnsiConsole.MarkupLine(
            $"[red]{Markup.Escape(challengeMessage.Content ?? "Unknown error")}[/]"
        );

        return;
    }


    if (challengeMessage.Type != "challenge")
    {
        AnsiConsole.MarkupLine(
            "[red]Expected authentication challenge.[/]"
        );

        return;
    }


    if (string.IsNullOrWhiteSpace(
        challengeMessage.Challenge))
    {
        AnsiConsole.MarkupLine(
            "[red]Server sent an empty challenge.[/]"
        );

        return;
    }


    // Decode challenge

    byte[] challenge =
        Convert.FromBase64String(
            challengeMessage.Challenge
        );


    // Sign challenge

    byte[] signature =
        rsa.SignData(
            challenge,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );


    string signatureBase64 =
        Convert.ToBase64String(
            signature
        );


    // Send signature

    await SendMessage(
        stream,
        new ChatMessage
        {
            Type = "challenge_response",
            Signature = signatureBase64
        }
    );


    ChatMessage? loginResponse =
        await ReadMessage(stream);


    if (loginResponse == null)
    {
        AnsiConsole.MarkupLine(
            "[red]Server disconnected.[]"
        );

        return;
    }


    if (loginResponse.Type == "error")
    {
        AnsiConsole.MarkupLine(
            $"[red]{Markup.Escape(loginResponse.Content ?? "Login failed.")}[/]"
        );

        return;
    }


    if (loginResponse.Type != "login_success")
    {
        AnsiConsole.MarkupLine(
            "[red]Login failed.[]"
        );

        return;
    }


    AnsiConsole.MarkupLine(
        $"[green]Logged in as {Markup.Escape(username)}![/]"
    );
}


// ============================================================
// CHAT
// ============================================================

List<string> messages = new();

StringBuilder input = new();

bool running = true;

Panel panel = CreatePanel();


// Background task receiving server messages

Task receiveTask = Task.Run(
    async () =>
    {
        while (running)
        {
            ChatMessage? message;

            try
            {
                message =
                    await ReadMessage(stream);
            }
            catch
            {
                break;
            }


            if (message == null)
            {
                break;
            }


            if (message.Type == "chat")
            {
                string sender =
                    Markup.Escape(
                        message.Username ?? "Unknown"
                    );

                string content =
                    Markup.Escape(
                        message.Content ?? ""
                    );

                messages.Add(
                    $"{sender}: {content}"
                );
            }
            else if (message.Type == "system")
            {
                string content =
                    Markup.Escape(
                        message.Content ?? ""
                    );

                messages.Add(
                    $"[grey]{content}[/]"
                );
            }
        }

        running = false;
    }
);

Task inputTask = Task.Run(HandleInput);
// ============================================================
// LIVE UI
// ============================================================

AnsiConsole.Live(panel)
    .Start(ctx =>
    {
        while (running)
        {
            panel = CreatePanel();

            ctx.UpdateTarget(panel);

            ctx.Refresh();

            Thread.Sleep(50);
        }
    });


// Wait for receive task

try
{
    await receiveTask;
}
catch
{
}


// ============================================================
// END
// ============================================================

AnsiConsole.MarkupLine(
    "[grey]Disconnected from server.[/]"
);


// ============================================================
// INPUT
// ============================================================

async Task HandleInput()
{
    while (running)
    {
        ConsoleKeyInfo key =
            Console.ReadKey(true);


        // ENTER

        if (key.Key == ConsoleKey.Enter)
        {
            string message =
                input.ToString();

            input.Clear();


            if (string.IsNullOrWhiteSpace(message))
            {
                continue;
            }


            // Quit

            if (message == "/quit")
            {
                try
                {
                    await SendMessage(
                        stream,
                        new ChatMessage
                        {
                            Type = "message",
                            Content = "/quit"
                        }
                    );
                }
                catch
                {
                }

                running = false;

                break;
            }


            // Normal message

            try
            {
                await SendMessage(
                    stream,
                    new ChatMessage
                    {
                        Type = "message",
                        Content = message
                    }
                );
            }
            catch
            {
                running = false;

                break;
            }
        }


        // BACKSPACE

        else if (key.Key == ConsoleKey.Backspace)
        {
            if (input.Length > 0)
            {
                input.Length--;
            }
        }


        // Normal character

        else if (!char.IsControl(key.KeyChar))
        {
            input.Append(key.KeyChar);
        }
    }
}


// ============================================================
// PANEL
// ============================================================

Panel CreatePanel()
{
    StringBuilder chatText =
        new();

    foreach (string message in messages)
    {
        chatText.AppendLine(message);
        chatText.AppendLine();
    }


    string currentInput =
        Markup.Escape(
            input.ToString()
        );


    chatText.Append(
        $"[grey]>[/] {currentInput}[grey]█[/]"
    );


    return new Panel(
        chatText.ToString()
    )
    .Header(
        $"[bold cyan] Chat — {Markup.Escape(username)} [/]"
    )
    .Expand()
    .Border(BoxBorder.Rounded);
}


// ============================================================
// ASK USERNAME
// ============================================================

string AskForUsername()
{
    AnsiConsole.Clear();

    AnsiConsole.Write(
        new FigletText("Classroom Chat")
    );

    AnsiConsole.WriteLine();

    AnsiConsole.MarkupLine(
        "[bold yellow]Choose your username:[/]"
    );

    AnsiConsole.MarkupLine(
        "[grey]Choose wisely, because you cannot change it![/]"
    );

    AnsiConsole.WriteLine();

    while (true)
    {
        string? username =
            AnsiConsole.Ask<string>(
                "[cyan]Username[/]"
            );

        username =
            username.Trim();


        if (username.Length < 3 ||
            username.Length > 20)
        {
            AnsiConsole.MarkupLine(
                "[red]Username must be 3-20 characters.[/]"
            );

            continue;
        }


        bool valid = true;

        foreach (char c in username)
        {
            if (!char.IsLetterOrDigit(c) &&
                c != '_')
            {
                valid = false;
                break;
            }
        }


        if (!valid)
        {
            AnsiConsole.MarkupLine(
                "[red]Only letters, numbers and underscore are allowed.[/]"
            );

            continue;
        }


        return username;
    }
}


// ============================================================
// SEND
// ============================================================

async Task SendMessage(
    NetworkStream stream,
    ChatMessage message)
{
    string json =
        JsonSerializer.Serialize(message)
        + "\n";

    byte[] data =
        Encoding.UTF8.GetBytes(json);

    await stream.WriteAsync(data);
}


// ============================================================
// READ
// ============================================================

async Task<ChatMessage?> ReadMessage(
    NetworkStream stream)
{
    using MemoryStream memory =
        new();

    byte[] buffer =
        new byte[1];


    while (true)
    {
        int bytesRead =
            await stream.ReadAsync(buffer);


        if (bytesRead == 0)
        {
            return null;
        }


        if (buffer[0] == '\n')
        {
            break;
        }


        memory.WriteByte(buffer[0]);


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
// FILE PATHS
// ============================================================

string GetUsernamePath()
{
    return Path.Combine(
        IdentityFolder,
        UsernameFile
    );
}


string GetPrivateKeyPath()
{
    return Path.Combine(
        IdentityFolder,
        PrivateKeyFile
    );
}


// ============================================================
// DATA CLASS
// ============================================================

class ChatMessage
{
    public string Type { get; set; } = "";

    public string? Username { get; set; }

    public string? PublicKey { get; set; }

    public string? Challenge { get; set; }

    public string? Signature { get; set; }

    public string? Content { get; set; }
}