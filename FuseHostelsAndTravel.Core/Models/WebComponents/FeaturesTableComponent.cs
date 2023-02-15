namespace FuseHostelsAndTravel.Core.Models.WebComponents
{
	public class FeaturesTableComponent
	{
		public string Title { get; private set; }
        public List<string> Features { get; private set; }
		public List<List<string>> FeaturesParsed { get; private set; }

        public FeaturesTableComponent()
        {

        }

        public FeaturesTableComponent(string title, List<string> features)
        {
            Title = title;
            Features = features;
            FeaturesParsed = new List<List<string>>();

            int featuresColumnsCount = 4;
            for (int i = 0; i < featuresColumnsCount; i++)
            {
                FeaturesParsed.Add(new List<string>());
            }

            if (features.Any())
            {
                int featuresParsedIndex = 0;
                for (int i = 0; i < features.Count; i++)
                {
                    FeaturesParsed[featuresParsedIndex].Add(features[i]);
                    featuresParsedIndex++;

                    if (featuresParsedIndex == 4)
                        featuresParsedIndex = 0;
                }
            }
        }
	}
}

