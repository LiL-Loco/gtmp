// Example game mode for the GTMP server.
//
// The engine implements a Pawn subset: no type tags, no classes, no preprocessor.
// Functions are declared with `Name(params)`, variables with `new`, and dotted
// callee names map straight onto natives registered by the server.

new tickCount = 0;
new joinCount = 0;

OnScriptLoad()
{
    Log("freeroam game mode loaded");
    Log(GetServerName());
    CreateVehicle("adder", 0.0, 0.0, 0.0);
    Log("spawned an adder at the origin");
    return true;
}

OnTick(tick)
{
    tickCount = tickCount + 1;

    // Once a second, say something, so the log shows the loop is alive.
    if (tickCount % 20 == 0)
    {
        Log("tick " + tick + ", players: " + GetPlayerCount());
    }

    return true;
}

OnPlayerConnect(playerId, name)
{
    joinCount = joinCount + 1;
    Log(name + " joined (connection #" + joinCount + ")");

    GivePlayerMoney(playerId, 5000);
    SendPlayerMessage(playerId, "Welcome, " + name + "!");

    return true;
}

OnPlayerDisconnect(playerId)
{
    Log("player " + playerId + " left");
    return true;
}

OnPlayerChat(playerId, name, text)
{
    Log(name + ": " + text);

    // Returning true marks the message as handled. Returning false lets the
    // server forward it to everybody else, which is the usual chat behaviour.
    BroadcastChat(playerId, text);

    return false;
}