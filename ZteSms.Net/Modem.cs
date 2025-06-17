namespace ZteSms.Net;

using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Net.Http.Headers;
using System.Text;
using System.Collections.Generic;
using System.Text.Json;

public class Modem
{
    private readonly HttpClient _httpClient = new();
    private readonly string _modemIP;
    private readonly string _modemPassword;
    private string _loginCookie = string.Empty;
    private string _modemVersion = string.Empty;

    public Modem(string modemIP = "192.168.0.1", string modemPassword = "")
    {
        _modemIP = modemIP;
        _modemPassword = modemPassword;
        _httpClient.BaseAddress = new Uri($"http://{_modemIP}");
        _httpClient.DefaultRequestHeaders.Referrer = new Uri($"http://{_modemIP}/index.html");
    }

    private async Task<HttpResponseMessage> RequestAsync(HttpMethod method, string path, Dictionary<string, string> data)
    {
        var content = new FormUrlEncodedContent(data ?? new());
        var request = new HttpRequestMessage(method, path);
        if (method == HttpMethod.Get && data is not null)
        {
            request.RequestUri = new Uri($"http://{_modemIP}{path}?{await content.ReadAsStringAsync()}");
        }
        else if (method == HttpMethod.Post)
        {
            request.Content = content;
        }
        if (!string.IsNullOrEmpty(_loginCookie))
        {
            request.Headers.Add("Cookie", _loginCookie);
        }
        return await _httpClient.SendAsync(request);
    }

    private async Task LoginAsync()
    {
        var data = new Dictionary<string, string>
        {
            ["isTest"] = "false",
            ["goformId"] = "LOGIN",
            ["password"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(_modemPassword))
        };
        var response = await RequestAsync(HttpMethod.Post, "/goform/goform_set_cmd_process", data);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!json.RootElement.TryGetProperty("result", out var result) || result.GetString() != "0")
            throw new Exception("Login to modem failed.");
        if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
            _loginCookie = cookies.First().Split(';')[0];
    }

    private async Task LogoutAsync()
    {
        var data = new Dictionary<string, string>
        {
            ["isTest"] = "false",
            ["goformId"] = "LOGOUT",
            ["AD"] = await GetADAsync()
        };
        await RequestAsync(HttpMethod.Post, "/goform/goform_set_cmd_process", data);
        _loginCookie = string.Empty;
    }

    private async Task<string> GetModemVersionAsync()
    {
        if (!string.IsNullOrEmpty(_modemVersion))
            return _modemVersion;
        var data = new Dictionary<string, string>
        {
            ["isTest"] = "false",
            ["cmd"] = "cr_version,wa_inner_version",
            ["multi_data"] = "1"
        };
        var response = await RequestAsync(HttpMethod.Get, "/goform/goform_get_cmd_process", data);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        string crStr = json.RootElement.TryGetProperty("cr_version", out var cr) ? cr.GetString() : string.Empty;
        string waStr = json.RootElement.TryGetProperty("wa_inner_version", out var wa) ? wa.GetString() : string.Empty;
        if (!string.IsNullOrEmpty(crStr) || !string.IsNullOrEmpty(waStr))
        {
            _modemVersion = $"{crStr}{waStr}";
            return _modemVersion;
        }
        throw new Exception("Getting modem version failed.");
    }

    private static string HexMD5(string input)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        var sb = new StringBuilder();
        foreach (var b in bytes)
            sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    private async Task<string> GetRDAsync()
    {
        var data = new Dictionary<string, string>
        {
            ["isTest"] = "false",
            ["cmd"] = "RD"
        };
        var response = await RequestAsync(HttpMethod.Get, "/goform/goform_get_cmd_process", data);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!json.RootElement.TryGetProperty("RD", out var rd))
            throw new Exception("Getting RD failed.");
        return rd.GetString();
    }

    private async Task<string> GetADAsync()
    {
        var version = await GetModemVersionAsync();
        var rd = await GetRDAsync();
        return HexMD5(HexMD5(version) + rd);
    }

    private async Task AwaitConfirmationAsync(int cmd)
    {
        for (int i = 0; i < 20; i++)
        {
            var info = await GetCmdStatusInfoAsync(cmd);
            if (info.TryGetProperty("sms_cmd_status_result", out var status) && status.GetString() != "0")
                return;
            await Task.Delay(200);
        }
        throw new Exception("Command confirmation timeout.");
    }

    private async Task<JsonElement> GetCmdStatusInfoAsync(int cmd)
    {
        var data = new Dictionary<string, string>
        {
            ["isTest"] = "false",
            ["cmd"] = "sms_cmd_status_info",
            ["sms_cmd"] = cmd.ToString()
        };
        var response = await RequestAsync(HttpMethod.Get, "/goform/goform_get_cmd_process", data);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    public async Task<JsonElement> GetSmsCapacityInfoAsync()
    {
        await LoginAsync();
        var data = new Dictionary<string, string>
        {
            ["isTest"] = "false",
            ["cmd"] = "sms_capacity_info"
        };
        var response = await RequestAsync(HttpMethod.Get, "/goform/goform_get_cmd_process", data);
        await LogoutAsync();
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    public async Task<List<JsonElement>> GetAllSmsAsync()
    {
        await LoginAsync();
        var data = new Dictionary<string, string>
        {
            ["isTest"] = "false",
            ["cmd"] = "sms_data_total",
            ["page"] = "0",
            ["data_per_page"] = "5000",
            ["mem_store"] = "1",
            ["tags"] = "10",
            ["order_by"] = "order by id desc"
        };
        var response = await RequestAsync(HttpMethod.Get, "/goform/goform_get_cmd_process", data);
        await LogoutAsync();
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var list = new List<JsonElement>();
        if (json.RootElement.TryGetProperty("messages", out var messages))
        {
            foreach (var msg in messages.EnumerateArray())
                list.Add(msg.Clone());
        }
        return list;
    }

    public async Task DeleteSmsAsync(IEnumerable<string> ids)
    {
        await LoginAsync();
        var data = new Dictionary<string, string>
        {
            ["isTest"] = "false",
            ["goformId"] = "DELETE_SMS",
            ["msg_id"] = string.Join(';', ids) + ";",
            ["notCallback"] = "true",
            ["AD"] = await GetADAsync()
        };
        var response = await RequestAsync(HttpMethod.Post, "/goform/goform_set_cmd_process", data);
        await AwaitConfirmationAsync(6);
        await LogoutAsync();
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!json.RootElement.TryGetProperty("result", out var result) || result.GetString() != "success")
            throw new Exception("Error deleting SMS.");
    }

    public async Task DeleteAllSmsAsync(string tag = "")
    {
        var messages = await GetAllSmsAsync();
        var ids = new List<string>();
        foreach (var msg in messages)
        {
            if (string.IsNullOrEmpty(tag) || msg.GetProperty("tag").GetString() == tag)
                ids.Add(msg.GetProperty("id").GetString());
        }
        await DeleteSmsAsync(ids);
    }

    public async Task SetSmsAsReadAsync(IEnumerable<string> ids)
    {
        await LoginAsync();
        var data = new Dictionary<string, string>
        {
            ["isTest"] = "false",
            ["goformId"] = "SET_MSG_READ",
            ["msg_id"] = string.Join(';', ids) + ";",
            ["tag"] = "0",
            ["AD"] = await GetADAsync()
        };
        var response = await RequestAsync(HttpMethod.Post, "/goform/goform_set_cmd_process", data);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!json.RootElement.TryGetProperty("result", out var result) || result.GetString() != "success")
            throw new Exception("Error marking SMS as read.");
        await AwaitConfirmationAsync(5);
        await LogoutAsync();
    }

    public async Task SetAllSmsAsReadAsync()
    {
        var messages = await GetAllSmsAsync();
        var ids = new List<string>();
        foreach (var msg in messages)
        {
            if (msg.GetProperty("tag").GetString() == "1")
                ids.Add(msg.GetProperty("id").GetString());
        }
        await SetSmsAsReadAsync(ids);
    }

    public async Task<JsonElement> SendSmsAsync(string number, string message)
    {
        await LoginAsync();
        var smsTime = DateTime.UtcNow.ToString("yy;MM;dd;HH;mm;ss;+0");
        var data = new Dictionary<string, string>
        {
            ["isTest"] = "false",
            ["goformId"] = "SEND_SMS",
            ["notCallback"] = "true",
            ["Number"] = number,
            ["sms_time"] = smsTime,
            ["MessageBody"] = EncodeMessage(message),
            ["ID"] = "-1",
            ["encode_type"] = "UNICODE",
            ["AD"] = await GetADAsync()
        };
        var response = await RequestAsync(HttpMethod.Post, "/goform/goform_set_cmd_process", data);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!json.RootElement.TryGetProperty("result", out var result) || result.GetString() != "success")
            throw new Exception("Error sending SMS.");
        await AwaitConfirmationAsync(4);
        var messages = await GetAllSmsAsync();
        foreach (var msg in messages)
        {
            if (msg.GetProperty("tag").GetString() == "2" &&
                msg.GetProperty("number").GetString() == number &&
                msg.GetProperty("content").GetString() == message)
            {
                await LogoutAsync();
                return msg;
            }
        }
        await LogoutAsync();
        throw new Exception("Sent SMS not found.");
    }

    private static string EncodeMessage(string message)
    {
        if (string.IsNullOrEmpty(message)) return string.Empty;
        var sb = new StringBuilder();
        foreach (var codepoint in message.EnumerateRunes())
        {
            var hex = ((int)codepoint.Value).ToString("X");
            sb.Append(hex.PadLeft(4, '0'));
        }
        return sb.ToString();
    }
}
