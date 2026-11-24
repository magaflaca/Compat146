# Compat146

TShock 6.2.1 plugin (Terraria 1.4.5.8) that lets Terraria 1.4.6 clients join.

- Accepts the 1.4.6 handshake (`Terraria329`) next to the normal one; 1.4.5.8 clients keep working.
- Translates the packets whose layout changed, only for 1.4.6 clients: 41 (item animation), 88 (item tweaker), 91 (emote bubble), 117 (player hurt).
- Extra data sent by 1.4.6 clients (packets 164/165, the new field in 41, the new bit in 117) is ignored by the old server.

1.4.6 clients only join over TCP here; the UDP crossplay transport needs a 1.4.6 server.

## Build

References `TShockAPI.dll`, `OTAPI.dll`, `TerrariaServer.dll` and `ModFramework.dll` from `C:\isab\ref`
(override with `-p:RefPath=<path>`):

```
dotnet build -c Release
```

Copy `bin/Release/Compat146.dll` to `ServerPlugins/`.
