﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace IRI.Ket.Common.Service
{
    public static class DateTimeServices
    {
        public static async Task<DateTime?> GetNowFromServer(WebProxy proxy = null)
        {
            WebClient client = new WebClient();

            if (proxy != null)
            {
                client.Proxy = proxy;
            }

            try
            {
                var response = await client.DownloadStringTaskAsync("https://nist.time.gov/actualtime.cgi?lzbc=siqm9b");
                string time = Regex.Match(response, @"(?<=\btime="")[^""]*").Value;
                double milliseconds = Convert.ToInt64(time) / 1000.0;
                var result = new DateTime(1970, 1, 1).AddMilliseconds(milliseconds).ToLocalTime();

                return result;
            }
            catch (Exception ex)
            {
                return null;
            }
        }
    }
}
