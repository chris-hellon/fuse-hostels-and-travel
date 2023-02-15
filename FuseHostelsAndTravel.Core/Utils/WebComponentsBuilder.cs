namespace FuseHostelsAndTravel.Core.Utils
{
	public static class WebComponentsBuilder
	{
		public static List<ContainerHalfImageTextComponent> GetHostelsContainers(List<Hostel> hostels)
		{
            var hostelsContainers = new List<ContainerHalfImageTextComponent>();
            bool swapDirection = true;

            foreach (var item in hostels)
            {
                hostelsContainers.Add(new ContainerHalfImageTextComponent(
                item.Name,
                item.ShortDescription,
                item.ImageUrl,
                swapDirection,
                swapDirection ? new OvalContainerComponent(item.Name.ConvertToCamelCase("Ovals"), null, -30, null, -25) : null,
                new ButtonComponent($"/Hostels/Index?hostelName={item.FriendlyUrl}", $"View {item.Name}")));

                swapDirection = swapDirection ? false : true;
            }

            return hostelsContainers;
        }

        public static AlternatingCardHeightComponent GetToursCards(List<Tour> tours, string animationStart = "onScroll", string title = "TAILORED TOURS", string body = "Explore the culture of hoi an with FUSE travel.")
        {
            var toursCards = new AlternatingCardHeightComponent(new GenericBannerComponent(title, body), null, animationStart);

            var animationDelay = 200M;
            foreach (var item in tours)
            {
                toursCards.Cards.Add(new CardComponent(
                    item.Name,
                    item.ShortDescription,
                    item.ImageUrl,
                    12, 6, 12,
                    animationDelay,
                    item != tours.Last() ? 4 : null,
                    item != tours.Last() ? 8 : null,
                    null,
                    new ButtonComponent("btn-outline-primary", $"/Tours/Index?tourName={item.FriendlyUrl}", "Find Out More"), animationStart));

                animationDelay += 200M;
            }

            return toursCards;
        }

        public static CarouselCardsComponent GetToursCarouselCards(List<Tour> tours, string animationStart = "onScroll", string title = "TAILORED TOURS", string body = "Explore the culture of hoi an with FUSE travel.")
        {
            var toursCards = new CarouselCardsComponent(new GenericBannerComponent(title, body), null, animationStart);

            var animationDelay = 200M;
            foreach (var item in tours)
            {
                toursCards.Cards.Add(new CardComponent(
                    item.Name,
                    item.ShortDescription,
                    item.ImageUrl,
                    null, null, null,
                    null,
                    null,
                    null,
                    null,
                    new ButtonComponent("btn-outline-primary align-bottom", $"/Tours/Index?tourName={item.FriendlyUrl}", "Find Out More")));

                animationDelay += 200M;
            }

            return toursCards;
        }

        public static CarouselCardsComponent GetEventsCarouselCards(List<Event> events)
        {
            var eventsCards = new CarouselCardsComponent(null, null, null, "_CardWithBackgroundPartial");

            var animationDelay = 200M;
            foreach (var item in events)
            {
                eventsCards.Cards.Add(new CardWithBackgroundComponent(item.Name,
                    item.ShortDescription,
                    item.BackgroundColor,
                    item.ImageUrl,
                    null, null, null, null));

                animationDelay += 200M;
            }

            return eventsCards;
        }

        public static FullImageCardComponent GetHostelAccommodationCards(List<HostelRoom> hostelRooms, string animationStart = "onScroll", string title = "ACCOMMODATION", string body = "All private & shared rooms at FUSE Beachside come with pool and ocean views as standard so there is no better place to stay if beach vibes is your thing.")
        {
            var accommodationCards = new FullImageCardComponent(new GenericBannerComponent(title, body, new List<OvalContainerComponent>()
            {
                new OvalContainerComponent("hostelAccommodationOvals1", -185, null, -40, null)
            }), null, animationStart);
            var animationDelay = 200M;

            for (int i = 0; i < hostelRooms.Count; i++)
            {
                var item = hostelRooms[i];
                accommodationCards.Cards.Add(new CardComponent(
                    item.Name,
                    item.Description,
                    item.ImageUrl,
                    new List<string>() { item.ImageUrl },
                    12, 6, 12,
                    animationDelay, 5, 10,
                    animationStart));

                animationDelay += 200M;
            }

            return accommodationCards;
        }

        public static NavPillsComponent GetHostelDirectionsNavPills(Hostel hostel)
        {
            return new NavPillsComponent($"GETTING TO {hostel.PageSubTitle.ToUpper()} {hostel.PageTitle.ToUpper()}", hostel.Directions.Select(x => new NavPill(x.Title, x.Content.Select(c => new NavPillContent(c.Body, c.Style)).ToList())).ToList());
        }

        public static FeaturesTableComponent GetHostelFacilities(Hostel hostel)
        {
            return new FeaturesTableComponent("Faciliities & Amenities", hostel.Facilities);
        }
    }
}