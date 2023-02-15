using System.Globalization;

namespace FuseHostelsAndTravel.Core.Utils
{
	public static class Extensions
    {
		public static string ConvertToCamelCase(this string value, string additionalValue = null)
		{
            var textinfo = new CultureInfo("en-US", false).TextInfo;
            var formattedTitle = "";

            if (value.Contains(" "))
            {
                var splitValues = value.Split(" ").ToList();

                for (int i = 0; i < splitValues.Count; i++)
                {
                    var splitValue = splitValues[i];
                    if (i == 0)
                        formattedTitle += splitValue.ToLower();
                    else
                        formattedTitle += textinfo.ToTitleCase(splitValue.ToLower());
                }
            }
            else formattedTitle = value.ToLower();

            if (additionalValue != null)
                formattedTitle += additionalValue;

            return formattedTitle;
        }
	}
}

