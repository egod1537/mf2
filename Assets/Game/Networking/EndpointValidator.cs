using System;

namespace MF2.Game.Networking
{
    public static class EndpointValidator
    {
        public static bool TryValidate(string host, string portText, out string normalizedHost, out ushort port, out string error)
        {
            normalizedHost = (host ?? string.Empty).Trim();
            port = 0;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(normalizedHost))
            {
                error = "IP 또는 hostname을 입력하세요.";
                return false;
            }

            if (normalizedHost.IndexOfAny(new[] { ' ', '\t', '\r', '\n' }) >= 0)
            {
                error = "주소에 공백을 사용할 수 없습니다.";
                return false;
            }

            if (!ushort.TryParse(portText, out port) || port == 0)
            {
                error = "Port는 1..65535 범위의 숫자여야 합니다.";
                return false;
            }

            return true;
        }
    }
}
