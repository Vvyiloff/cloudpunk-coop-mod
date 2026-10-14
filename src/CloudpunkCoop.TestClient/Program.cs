using System.Diagnostics;
using CloudpunkCoop.Protocol;

// Stands in for the guest machine so the host's streaming can be verified without a second
// copy of the game. It speaks the real protocol: connects, sends HELLO, and reports what it
// receives, including the snapshot rate and whether the poses are actually moving.
//
//   dotnet run --project src\CloudpunkCoop.TestClient -- 127.0.0.1 27050 12

string host = args.Length > 0 ? args[0] : "127.0.0.1";
int port = args.Length > 1 ? int.Parse(args[1]) : 27050;
int seconds = args.Length > 2 ? int.Parse(args[2]) : 10;

Console.WriteLine($"connecting to {host}:{port} ...");

CoopConnection connection;
try
{
    connection = CoopConnection.Connect(host, port);
}
catch (Exception ex)
{
    Console.WriteLine($"connect failed: {ex.Message}");
    return 2;
}

using (connection)
{
    Console.WriteLine("connected, sending HELLO");
    connection.Send(new HelloMessage(ProtocolConstants.ProtocolVersion, "test-client"));

    var clock = Stopwatch.StartNew();
    long snapshots = 0;
    double firstAt = -1;
    double lastAt = -1;
    CarStateMessage last = default;
    double travelled = 0;
    float previousX = 0, previousY = 0, previousZ = 0;
    bool havePrevious = false;
    var otherMessages = new List<string>();

    while (clock.Elapsed.TotalSeconds < seconds)
    {
        if (!connection.TryReceive(out IMessage? message) || message == null)
        {
            Thread.Sleep(2);
            continue;
        }

        if (message is CarStateMessage car)
        {
            snapshots++;
            if (firstAt < 0)
            {
                firstAt = clock.Elapsed.TotalSeconds;
            }
            lastAt = clock.Elapsed.TotalSeconds;

            if (havePrevious)
            {
                double dx = car.PositionX - previousX;
                double dy = car.PositionY - previousY;
                double dz = car.PositionZ - previousZ;
                travelled += Math.Sqrt(dx * dx + dy * dy + dz * dz);
            }

            previousX = car.PositionX;
            previousY = car.PositionY;
            previousZ = car.PositionZ;
            havePrevious = true;
            last = car;

            if (snapshots % 20 == 1)
            {
                Console.WriteLine($"  {car}");
            }
        }
        else
        {
            otherMessages.Add(message.ToString() ?? message.Opcode.ToString());
            Console.WriteLine($"  {message}");
        }
    }

    Console.WriteLine();
    if (snapshots == 0)
    {
        Console.WriteLine("FAIL: no car snapshots received");
        return 1;
    }

    double span = lastAt - firstAt;
    double rate = span > 0.05 ? (snapshots - 1) / span : 0;
    Console.WriteLine($"snapshots={snapshots}  observedRate={rate:0.0} Hz  travelled={travelled:0.0} m");
    Console.WriteLine($"last={last}");
    Console.WriteLine($"other messages: {(otherMessages.Count == 0 ? "none" : string.Join(", ", otherMessages))}");

    bool rateOk = rate >= 15 && rate <= 25;
    Console.WriteLine(rateOk ? "PASS: snapshot rate is in the 19-21 Hz band" : "FAIL: snapshot rate outside 15-25 Hz");
    return rateOk ? 0 : 1;
}
