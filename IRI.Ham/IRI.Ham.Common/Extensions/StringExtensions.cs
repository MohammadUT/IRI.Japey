﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace System
{
    public static class StringExtensions
    {
        const char arabicYa01 = '\u0649';
        const char arabicYa02 = '\u064A';
        const char arabicKaf = '\u0643';

        const char farsiYa = '\u06CC';
        const char farsiKaf = '\u06A9';

        public static string ArabicToFarsi(this string arabicString)
        {
            if (string.IsNullOrWhiteSpace(arabicString))
                return arabicString;

            return arabicString
                .Replace(arabicYa01, farsiYa)
                .Replace(arabicYa02, farsiYa)
                .Replace(arabicKaf, farsiKaf);
        }

        public static string LatinNumbersToFarsiNumbers(this string value)
        {
            return value.Replace('1', '۱')
                    .Replace('2', '۲')
                    .Replace('3', '۳')
                    .Replace('4', '۴')
                    .Replace('5', '۵')
                    .Replace('6', '۶')
                    .Replace('7', '۷')
                    .Replace('8', '۸')
                    .Replace('9', '۹')
                    .Replace('0', '۰')
                    .Replace('.', '\u066B');
        }

    }
}
