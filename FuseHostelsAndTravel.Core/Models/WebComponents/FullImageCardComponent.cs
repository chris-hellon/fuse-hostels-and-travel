namespace FuseHostelsAndTravel.Core.Models.WebComponents
{
	public class FullImageCardComponent
    {
        public GenericBannerComponent Header { get; private set; }
        public List<CardComponent> Cards { get; private set; }
        public string AnimationStart { get; private set; }

        public FullImageCardComponent()
        {

        }

        public FullImageCardComponent(GenericBannerComponent header, List<CardComponent> cards = null, string animationStart = "onScroll")
        {
            Cards = cards ?? new List<CardComponent>();
            Header = header;
            AnimationStart = animationStart;
        }
    }
}

