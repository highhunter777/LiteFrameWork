using System;
using System.Globalization;

namespace LiteGame
{
    /// <summary>
    /// 房间服端点（票据/投影下发的 Address="host:port" 拆解）。
    /// </summary>
    public sealed class RoomEndpoint
    {
        public string Host;
        public int Port;

        /// <summary>解析 "host:port"；失败给 reason（消费端按确定错误态展示，不猜默认）。</summary>
        public static bool TryParse(string address, out RoomEndpoint endpoint, out string reason)
        {
            endpoint = null;
            reason = null;
            if (string.IsNullOrWhiteSpace(address)) { reason = "地址为空"; return false; }

            string host;
            string portText;
            if (address[0] == '[')
            {
                // [IPv6]:port。
                int close = address.IndexOf(']');
                if (close < 0 || close + 1 >= address.Length || address[close + 1] != ':')
                { reason = "括号形态缺端口"; return false; }
                host = address.Substring(1, close - 1);
                portText = address.Substring(close + 2);
            }
            else
            {
                int colon = address.LastIndexOf(':');
                if (colon <= 0 || colon + 1 >= address.Length) { reason = "缺端口"; return false; }
                host = address.Substring(0, colon);
                portText = address.Substring(colon + 1);
            }

            if (!int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int port)
                || port < 1 || port > 65535)
            { reason = "端口非法"; return false; }

            endpoint = new RoomEndpoint { Host = host, Port = port };
            return true;
        }
    }
}
