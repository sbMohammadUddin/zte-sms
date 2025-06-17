namespace ZteSms;

using System.Text.Json.Serialization;

/// <summary>
/// Possible values for <see cref="Message.tag"/>
/// </summary>
public enum MessageTag
{
    /// <summary>Received SMS</summary>
    Received = 0,
    /// <summary>Unread received SMS</summary>
    UnreadReceived = 1,
    /// <summary>Sent SMS</summary>
    Sent = 2,
    /// <summary>Failed sent SMS</summary>
    FailedSent = 3,
    /// <summary>Draft SMS</summary>
    Draft = 4
}

public class Message
{
    public string id { get; set; } = string.Empty;
    public string number { get; set; } = string.Empty;
    public string content { get; set; } = string.Empty;
    public string tag { get; set; } = string.Empty;
    public string date { get; set; } = string.Empty;
    public string draft_group_id { get; set; } = string.Empty;
    public string received_all_concat_sms { get; set; } = string.Empty;
    public string concat_sms_total { get; set; } = string.Empty;
    public string concat_sms_received { get; set; } = string.Empty;
    public string sms_class { get; set; } = string.Empty;

    [JsonIgnore]
    public MessageTag Tag => Enum.TryParse<MessageTag>(tag, out MessageTag value)
        ? value
        : throw new InvalidOperationException($"Unknown tag: {tag}");
}

public class SmsCapacityInfo
{
    public string sms_nv_total { get; set; }
    public string sms_sim_total { get; set; }
    public string sms_nvused_total { get; set; }
    public string sms_nv_rev_total { get; set; }
    public string sms_nv_send_total { get; set; }
    public string sms_nv_draftbox_total { get; set; }
    public string sms_sim_rev_total { get; set; }
    public string sms_sim_send_total { get; set; }
    public string sms_sim_draftbox_total { get; set; }
}

internal class ResultResponse
{
    public string? result { get; set; }
}

internal class VersionResponse
{
    public string? cr_version { get; set; }
    public string? wa_inner_version { get; set; }
}

internal class RdResponse
{
    public string? RD { get; set; }
}

internal class CmdStatusInfo
{
    public string? sms_cmd_status_result { get; set; }
}

internal class AllSmsResponse
{
    public List<Message>? messages { get; set; }
}
