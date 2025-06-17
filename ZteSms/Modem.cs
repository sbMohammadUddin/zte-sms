namespace ZteSms;

using System;
using System.Net.Http;
using System.Threading.Tasks;
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
        FormUrlEncodedContent content = new(data ?? new());
        HttpRequestMessage request = new(method, path);
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

    private async Task LoginAsync(bool isTest = false)
    {
        Dictionary<string, string> data = new()
        {
            ["isTest"] = isTest.ToString(),
            ["goformId"] = "LOGIN",
            ["password"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(_modemPassword))
        };
        HttpResponseMessage response = await RequestAsync(HttpMethod.Post, "/goform/goform_set_cmd_process", data);
        ResultResponse? json = JsonSerializer.Deserialize<ResultResponse>(await response.Content.ReadAsStringAsync());
        if (json?.result != "0")
        {
            throw new Exception("Login to modem failed.");
        }

        if (response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies))
        {
            _loginCookie = cookies.First().Split(';')[0];
        }
    }

    private async Task LogoutAsync(bool isTest = false)
    {
        Dictionary<string, string> data = new()
        {
            ["isTest"] = isTest.ToString(),
            ["goformId"] = "LOGOUT",
            ["AD"] = await GetADAsync()
        };
        await RequestAsync(HttpMethod.Post, "/goform/goform_set_cmd_process", data);
        _loginCookie = string.Empty;
    }

    private async Task<string> GetModemVersionAsync(bool isTest = false)
    {
        if (!string.IsNullOrEmpty(_modemVersion))
        {
            return _modemVersion;
        }

        Dictionary<string, string> data = new()
        {
            ["isTest"] = isTest.ToString(),
            ["cmd"] = "cr_version,wa_inner_version",
            ["multi_data"] = "1"
        };
        HttpResponseMessage response = await RequestAsync(HttpMethod.Get, "/goform/goform_get_cmd_process", data);
        VersionResponse? json = JsonSerializer.Deserialize<VersionResponse>(await response.Content.ReadAsStringAsync());
        string crStr = json?.cr_version ?? string.Empty;
        string waStr = json?.wa_inner_version ?? string.Empty;
        if (!string.IsNullOrEmpty(crStr) || !string.IsNullOrEmpty(waStr))
        {
            _modemVersion = $"{crStr}{waStr}";
            return _modemVersion;
        }
        throw new Exception("Getting modem version failed.");
    }

    private static string HexMD5(string input)
    {
        byte[] bytes = System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes(input));
        StringBuilder sb = new();
        foreach (byte b in bytes)
        {
            sb.Append(b.ToString("x2"));
        }

        return sb.ToString();
    }

    private async Task<string> GetRDAsync(bool isTest = false)
    {
        Dictionary<string, string> data = new()
        {
            ["isTest"] = isTest.ToString(),
            ["cmd"] = "RD"
        };
        HttpResponseMessage response = await RequestAsync(HttpMethod.Get, "/goform/goform_get_cmd_process", data);
        RdResponse? json = JsonSerializer.Deserialize<RdResponse>(await response.Content.ReadAsStringAsync());
        if (string.IsNullOrEmpty(json?.RD))
        {
            throw new Exception("Getting RD failed.");
        }

        return json.RD;
    }

    private async Task<string> GetADAsync()
    {
        string version = await GetModemVersionAsync();
        string rd = await GetRDAsync();
        return HexMD5(HexMD5(version) + rd);
    }

    private async Task AwaitConfirmationAsync(int cmd)
    {
        for (int i = 0; i < 20; i++)
        {
            CmdStatusInfo? info = await GetCmdStatusInfoAsync(cmd);
            if (!string.IsNullOrEmpty(info?.sms_cmd_status_result) && info.sms_cmd_status_result != "0")
            {
                return;
            }

            await Task.Delay(200);
        }
        throw new Exception("Command confirmation timeout.");
    }

    private async Task<CmdStatusInfo?> GetCmdStatusInfoAsync(int cmd, bool isTest = false)
    {
        Dictionary<string, string> data = new()
        {
            ["isTest"] = isTest.ToString(),
            ["cmd"] = "sms_cmd_status_info",
            ["sms_cmd"] = cmd.ToString()
        };
        HttpResponseMessage response = await RequestAsync(HttpMethod.Get, "/goform/goform_get_cmd_process", data);
        return JsonSerializer.Deserialize<CmdStatusInfo>(await response.Content.ReadAsStringAsync());
    }

    public async Task<SmsCapacityInfo?> GetSmsCapacityInfoAsync(bool isTest = false)
    {
        await LoginAsync();
        Dictionary<string, string> data = new()
        {
            ["isTest"] = isTest.ToString(),
            ["cmd"] = "sms_capacity_info"
        };
        HttpResponseMessage response = await RequestAsync(HttpMethod.Get, "/goform/goform_get_cmd_process", data);
        await LogoutAsync();
        return JsonSerializer.Deserialize<SmsCapacityInfo>(await response.Content.ReadAsStringAsync());
    }

    public async Task<List<Message>> GetAllSmsAsync(bool isTest = false)
    {
        await LoginAsync();
        Dictionary<string, string> data = new()
        {
            ["isTest"] = isTest.ToString(),
            ["cmd"] = "sms_data_total",
            ["page"] = "0",
            ["data_per_page"] = "5000",
            ["mem_store"] = "1",
            ["tags"] = "10",
            ["order_by"] = "order by id desc"
        };
        HttpResponseMessage response = await RequestAsync(HttpMethod.Get, "/goform/goform_get_cmd_process", data);
        await LogoutAsync();
        AllSmsResponse? json = JsonSerializer.Deserialize<AllSmsResponse>(await response.Content.ReadAsStringAsync());
        return json?.messages ?? new List<Message>();
    }

    public async Task DeleteSmsAsync(IEnumerable<string> ids, bool isTest = false)
    {
        await LoginAsync();
        Dictionary<string, string> data = new()
        {
            ["isTest"] = isTest.ToString(),
            ["goformId"] = "DELETE_SMS",
            ["msg_id"] = string.Join(';', ids) + ";",
            ["notCallback"] = "true",
            ["AD"] = await GetADAsync()
        };
        HttpResponseMessage response = await RequestAsync(HttpMethod.Post, "/goform/goform_set_cmd_process", data);
        await AwaitConfirmationAsync(6);
        await LogoutAsync();
        ResultResponse? json = JsonSerializer.Deserialize<ResultResponse>(await response.Content.ReadAsStringAsync());
        if (json?.result != "success")
        {
            throw new Exception("Error deleting SMS.");
        }
    }

    public async Task DeleteAllSmsAsync(MessageTag? tag = null)
    {
        List<Message> messages = await GetAllSmsAsync();
        List<string> ids = new();
        foreach (Message msg in messages)
        {
            if (tag is null || msg.Tag == tag)
            {
                ids.Add(msg.id);
            }
        }
        await DeleteSmsAsync(ids);
    }

    public async Task SetSmsAsReadAsync(IEnumerable<string> ids, bool isTest = false)
    {
        await LoginAsync();
        Dictionary<string, string> data = new()
        {
            ["isTest"] = isTest.ToString(),
            ["goformId"] = "SET_MSG_READ",
            ["msg_id"] = string.Join(';', ids) + ";",
            ["tag"] = MessageTag.Received.ToString(),
            ["AD"] = await GetADAsync()
        };
        HttpResponseMessage response = await RequestAsync(HttpMethod.Post, "/goform/goform_set_cmd_process", data);
        ResultResponse? json = JsonSerializer.Deserialize<ResultResponse>(await response.Content.ReadAsStringAsync());
        if (json?.result != "success")
        {
            throw new Exception("Error marking SMS as read.");
        }

        await AwaitConfirmationAsync(5);
        await LogoutAsync();
    }

    public async Task SetAllSmsAsReadAsync()
    {
        List<Message> messages = await GetAllSmsAsync();
        List<string> ids = new();
        foreach (Message msg in messages)
        {
            if (msg.Tag == MessageTag.UnreadReceived)
            {
                ids.Add(msg.id);
            }
        }
        await SetSmsAsReadAsync(ids);
    }

    public async Task<Message> SendSmsAsync(string number, string message, bool isTest = false)
    {
        await LoginAsync();
        string smsTime = DateTime.UtcNow.ToString("yy;MM;dd;HH;mm;ss;+0");
        Dictionary<string, string> data = new()
        {
            ["isTest"] = isTest.ToString(),
            ["goformId"] = "SEND_SMS",
            ["notCallback"] = "true",
            ["Number"] = number,
            ["sms_time"] = smsTime,
            ["MessageBody"] = EncodeMessage(message),
            ["ID"] = "-1",
            ["encode_type"] = "UNICODE",
            ["AD"] = await GetADAsync()
        };
        HttpResponseMessage response = await RequestAsync(HttpMethod.Post, "/goform/goform_set_cmd_process", data);
        ResultResponse? json = JsonSerializer.Deserialize<ResultResponse>(await response.Content.ReadAsStringAsync());
        if (json?.result != "success")
        {
            throw new Exception("Error sending SMS.");
        }

        await AwaitConfirmationAsync(4);
        List<Message> messages = await GetAllSmsAsync();
        foreach (Message msg in messages)
        {
            if (msg.Tag == MessageTag.Sent &&
                msg.number == number &&
                msg.content == message)
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
        if (string.IsNullOrEmpty(message))
        {
            return string.Empty;
        }

        StringBuilder sb = new();
        foreach (Rune codepoint in message.EnumerateRunes())
        {
            string hex = codepoint.Value.ToString("X");
            sb.Append(hex.PadLeft(4, '0'));
        }
        return sb.ToString();
    }
}
