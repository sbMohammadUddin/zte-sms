# ZTE-SMS

ZTE-SMS is a .NET library for interacting with ZTE LTE modems.

Tested with model `MF79U`.

## Build

```bash
dotnet build ZteSms.Net/ZteSms.Net.csproj -c Release
```

## Usage

```csharp
using ZteSms.Net;

var modem = new Modem("192.168.0.1", "password");
var messages = await modem.GetAllSmsAsync();
```
