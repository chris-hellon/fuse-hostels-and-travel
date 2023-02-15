namespace FuseHostelsAndTravel.Core.Models.WebComponents
{
	public class CardWithBackgroundComponent : CardComponent
    {
		public string BackgroundColor { get; private set; }

        public CardWithBackgroundComponent()
        {

        }

        public CardWithBackgroundComponent(string title, string body, string backgroundColor, string imageSrc, int? mdClass = null, int? lgClass = null, int? xsClass = null, int? marginBottom = null, int? marginTop = null, int? marginLeft = null, int? marginRight = null, int? paddingBottom = null, int? paddingTop = null, int? paddingLeft = null, int? paddingRight = null) : base(title, body, imageSrc, mdClass, lgClass, xsClass, marginBottom, marginTop, marginLeft, marginRight, paddingBottom, paddingTop, paddingLeft, paddingRight)
        {
            BackgroundColor = backgroundColor;
            AnimationDelay = null;
        }

        public CardWithBackgroundComponent(string title, string body, string backgroundColor, string imageSrc, int? mdClass = null, int? lgClass = null, int? xsClass = null, decimal? animationDelay = null, int? marginBottom = null, int? marginBottomLg = null) : base(title, body, imageSrc, mdClass, lgClass, xsClass, animationDelay, null, marginBottom, marginBottomLg)
        {
            BackgroundColor = backgroundColor;
        }
    }
}

