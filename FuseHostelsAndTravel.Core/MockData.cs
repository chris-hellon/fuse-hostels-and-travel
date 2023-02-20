namespace FuseHostelsAndTravel.Core
{
    public interface IMockData
    {
        Task<List<Hostel>> GetHostels();
        Task<List<HostelDirection>> GetHostelDirections(int hostelId);
        Task<List<HostelRoom>> GetHostelRooms(int? hostelId = null);
        Task<List<Tour>> GetTours(int? hostelId = null);
        Task<List<Event>> GetEvents();
        Task<List<Image>> GetHomePageCarouselImages();
    }

    public class MockData : IMockData
    {
        private List<Hostel> _hostels;
        protected List<Hostel> Hostels
        {
            get
            {
                if (_hostels == null)
                    _hostels = GetHostels().Result;

                return _hostels;
            }
        }

        public async Task<List<Hostel>> GetHostels()
        {
            var hostels = new List<Hostel>();
            await Task.Run(() =>
            {
                hostels = new List<Hostel>()
                {
                    new Hostel(1, "Hoi An Beachside",
                    "<p>Fuse Beachside offers boutique style design, travel lounge, large swimming pool & bar, ocean views and its very own beach club!</p><p>Only 10 minutes away from the historic Old Town you are still conveniently located to explore and enjoy all the sights that Hoi An serves up.</p>",
                    "<p>FUSE Beachside offers boutique style design, travel lounge, large swimming pool & bar, ocean views and its very own beach club!</p>",
                    "25 Nguyễn Phan Vinh",
                    "Cẩm An, Hội An, Quảng Nam",
                    "+84 37 2089310",
                    "ChIJA9kGrvENQjER7xWQvv5qnwo",
                    new List<string>() { "Bar & Restaurant", "Shared Dorms", "Luggage Storage", "WiFi", "Beach Views", "Private Rooms", "Lock Boxes", "Towels"},
                    "https://cf.bstatic.com/xdata/images/hotel/max1280x900/408321136.jpg?k=622f148e84487ab127a88d210e2f107b93ff89332f4931bb04a6be7ae50ecd7d&o=&hp=1",
                    "Hoi An", "Beachside", null, null, "VYJi8r"),
                    new Hostel(2, "Hoi An Old Town",
                    "<p>FUSE Old Town is in the perfect location to explore the famous UESCO world heritage Old Town. Surrounded by great street food & bars.</p>",
                    "<p>FUSE Old Town is in the perfect location to explore the famous UESCO world heritage Old Town. Surrounded by great street food & bars.</p>",
                    "25 Nguyễn Phan Vinh",
                    "Cẩm An, Hội An, Quảng Nam",
                    "+84 37 2089310",
                    "ChIJA9kGrvENQjER7xWQvv5qnwo",
                    new List<string>() { "Bar & Restaurant", "Shared Dorms", "Luggage Storage", "WiFi", "Beach Views", "Private Rooms", "Lock Boxes", "Towels"},
                    "https://vietnambackpackerhostels.azureedge.net/main/hoi-an-old-town.png",
                    "Hoi An", "Old Town", null, null, "nAuZCf"),
                    new Hostel(3, "Nha Trang Beachside",
                    "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
                    "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
                     "25 Nguyễn Phan Vinh",
                    "Cẩm An, Hội An, Quảng Nam",
                    "+84 37 2089310",
                    "ChIJA9kGrvENQjER7xWQvv5qnwo",
                    new List<string>() { "Bar & Restaurant", "Shared Dorms", "Luggage Storage", "WiFi", "Beach Views", "Private Rooms", "Lock Boxes", "Towels"},
                    "https://vietnambackpackerhostels.azureedge.net/main/nha-trang-beachside.png",
                    "Nha Trang", "Beachside")
                };
            });

            return hostels;
        }

        public async Task<List<HostelDirection>> GetHostelDirections(int hostelId)
        {
            var hostelDirections = new List<HostelDirection>();

            await Task.Run(() =>
            {
                var hostel = Hostels.FirstOrDefault(x => x.Id.HasValue && x.Id.Value == hostelId);

                if (hostel != null)
                {
                    var hostelAddressGoogleFormat = $"{hostel.AddressLine1.Replace(" ", "+").Replace(",", "")}+{hostel.AddressLine2.Replace(" ", "+").Replace(",", "")}";

                    hostelDirections = new List<HostelDirection>()
                    {
                        new HostelDirection(1, 1, "HOW TO FIND US", new List<HostelDirectionContent>()
                        {
                            new HostelDirectionContent(1, 1, $@"<h3>Where is FUSE {hostel.PageSubTitle} {hostel.PageTitle}</h3>
                                                <p>Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud.</p>
                                                <p>Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident.</p>
                                                <h5>FUSE {hostel.PageSubTitle}</h5>
                                                <p>{hostel.AddressLine1}<br />{hostel.AddressLine2}</p>
                                                <a class=""text-decoration-underline text-primary d-block mt-3"" target=""_blank"" href=""http://maps.google.com?saddr=My+Location&daddr={hostelAddressGoogleFormat}"">DIRECTIONS</a>"),
                            new HostelDirectionContent(2, 1, $@"<iframe id=""google-map"" class=""w-100 h-100 m-0 p-0 rounded"" frameborder=""0"" style=""border:0"" src=""https://www.google.com/maps/embed/v1/place?q=place_id:{hostel.GoogleMapsPlaceId}&key=AIzaSyAiYLmSCzYGXOdsJwXiu8GZ9c9UnHTtlgQ"" allowfullscreen=""""></iframe>",
                            "-webkit-filter: grayscale(100%); filter: grayscale(100%);")
                        }),
                        new HostelDirection(2, 1, "GETTING HERE FROM DA NANG", new List<HostelDirectionContent>()
                        {
                             new HostelDirectionContent(3, 2, $@"<h3>Where is FUSE {hostel.PageSubTitle} {hostel.PageTitle}</h3>
                                                <p>Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud.</p>
                                                <p>Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident.</p>
                                                <h5>FUSE {hostel.PageSubTitle}</h5>
                                                <p>{hostel.AddressLine1}<br />{hostel.AddressLine2}</p>"),
                             new HostelDirectionContent(4, 2, $@"<iframe id=""google-map"" class=""w-100 h-100 m-0 p-0 rounded"" frameborder=""0"" style=""border:0"" src=""https://www.google.com/maps/embed/v1/directions?key=AIzaSyAiYLmSCzYGXOdsJwXiu8GZ9c9UnHTtlgQ&&origin=Da+Nang+Vietnam&destination={hostelAddressGoogleFormat}&avoid=tolls|highways"" allowfullscreen=""""></iframe>",
                             "-webkit-filter: grayscale(100%); filter: grayscale(100%);")
                        }),
                        new HostelDirection(3, 2, "HOW TO FIND US", new List<HostelDirectionContent>()
                        {
                            new HostelDirectionContent(1, 1, $@"<h3>Where is FUSE {hostel.PageSubTitle} {hostel.PageTitle}</h3>
                                                <p>Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud.</p>
                                                <p>Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident.</p>
                                                <h5>FUSE {hostel.PageSubTitle}</h5>
                                                <p>{hostel.AddressLine1}<br />{hostel.AddressLine2}</p>
                                                <a class=""text-decoration-underline text-primary d-block mt-3"" target=""_blank"" href=""http://maps.google.com?saddr=My+Location&daddr={hostelAddressGoogleFormat}"">DIRECTIONS</a>"),
                            new HostelDirectionContent(2, 1, $@"<iframe id=""google-map"" class=""w-100 h-100 m-0 p-0 rounded"" frameborder=""0"" style=""border:0"" src=""https://www.google.com/maps/embed/v1/place?q=place_id:{hostel.GoogleMapsPlaceId}&key=AIzaSyAiYLmSCzYGXOdsJwXiu8GZ9c9UnHTtlgQ"" allowfullscreen=""""></iframe>",
                            "-webkit-filter: grayscale(100%); filter: grayscale(100%);")
                        }),
                        new HostelDirection(4, 2, "GETTING HERE FROM DA NANG", new List<HostelDirectionContent>()
                        {
                             new HostelDirectionContent(3, 2, $@"<h3>Where is FUSE {hostel.PageSubTitle} {hostel.PageTitle}</h3>
                                                <p>Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud.</p>
                                                <p>Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident.</p>
                                                <h5>FUSE {hostel.PageSubTitle}</h5>
                                                <p>{hostel.AddressLine1}<br />{hostel.AddressLine2}</p>"),
                             new HostelDirectionContent(4, 2, $@"<iframe id=""google-map"" class=""w-100 h-100 m-0 p-0 rounded"" frameborder=""0"" style=""border:0"" src=""https://www.google.com/maps/embed/v1/directions?key=AIzaSyAiYLmSCzYGXOdsJwXiu8GZ9c9UnHTtlgQ&&origin=Da+Nang+Vietnam&destination={hostelAddressGoogleFormat}&avoid=tolls|highways"" allowfullscreen=""""></iframe>",
                             "-webkit-filter: grayscale(100%); filter: grayscale(100%);")
                        }),
                        new HostelDirection(5, 3, "HOW TO FIND US", new List<HostelDirectionContent>()
                        {
                            new HostelDirectionContent(1, 1, $@"<h3>Where is FUSE {hostel.PageSubTitle} {hostel.PageTitle}</h3>
                                                <p>Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud.</p>
                                                <p>Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident.</p>
                                                <h5>FUSE {hostel.PageSubTitle}</h5>
                                                <p>{hostel.AddressLine1}<br />{hostel.AddressLine2}</p>
                                                <a class=""text-decoration-underline text-primary d-block mt-3"" target=""_blank"" href=""http://maps.google.com?saddr=My+Location&daddr={hostelAddressGoogleFormat}"">DIRECTIONS</a>"),
                            new HostelDirectionContent(2, 1, $@"<iframe id=""google-map"" class=""w-100 h-100 m-0 p-0 rounded"" frameborder=""0"" style=""border:0"" src=""https://www.google.com/maps/embed/v1/place?q=place_id:{hostel.GoogleMapsPlaceId}&key=AIzaSyAiYLmSCzYGXOdsJwXiu8GZ9c9UnHTtlgQ"" allowfullscreen=""""></iframe>",
                            "-webkit-filter: grayscale(100%); filter: grayscale(100%);")
                        }),
                        new HostelDirection(6, 3, "GETTING HERE FROM DA NANG", new List<HostelDirectionContent>()
                        {
                             new HostelDirectionContent(3, 2, $@"<h3>Where is FUSE {hostel.PageSubTitle} {hostel.PageTitle}</h3>
                                                <p>Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud.</p>
                                                <p>Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident.</p>
                                                <h5>FUSE {hostel.PageSubTitle}</h5>
                                                <p>{hostel.AddressLine1}<br />{hostel.AddressLine2}</p>"),
                             new HostelDirectionContent(4, 2, $@"<iframe id=""google-map"" class=""w-100 h-100 m-0 p-0 rounded"" frameborder=""0"" style=""border:0"" src=""https://www.google.com/maps/embed/v1/directions?key=AIzaSyAiYLmSCzYGXOdsJwXiu8GZ9c9UnHTtlgQ&&origin=Da+Nang+Vietnam&destination={hostelAddressGoogleFormat}&avoid=tolls|highways"" allowfullscreen=""""></iframe>",
                             "-webkit-filter: grayscale(100%); filter: grayscale(100%);")
                        })
                    };

                    hostelDirections = hostelDirections.Where(x => x.HostelId == hostelId).ToList();
                }
            });

            return hostelDirections;
        }

        public async Task<List<HostelRoom>> GetHostelRooms(int? hostelId = null)
        {
            var hostelRooms = new List<HostelRoom>();

            await Task.Run(() =>
            {
                hostelRooms = new List<HostelRoom>()
                {
                    new HostelRoom(1, 1, "Shared Dorms", "<p>Hoi An inspired shared rooms come with fully equipped bathrooms and brand new extra comfy beds for maximum rest when you most need it. Reading lights, charging dock & lock spaces are also provided.</p>", "", "https://cf.bstatic.com/xdata/images/hotel/max1280x900/408321158.jpg?k=6c595f92c6aa40181ba9b04bfae30abe5614363366a0c9cc5b9b3d48f36a6aff&o=&hp=1"),
                    new HostelRoom(2, 1, "Twin private rooms", "<p>Hoi An inspired shared rooms come with fully equipped bathrooms and brand new extra comfy beds for maximum rest when you most need it. Reading lights, charging dock & lock spaces are also provided.</p>", "", "https://cf.bstatic.com/xdata/images/hotel/max1280x900/408472105.jpg?k=610d7f59a8ab25e54c7a18daa5e4094cffd598221500e172708af48d8311ae8d&o=&hp=1"),
                    new HostelRoom(3, 1, "Double private rooms", "<p>Hoi An inspired shared rooms come with fully equipped bathrooms and brand new extra comfy beds for maximum rest when you most need it. Reading lights, charging dock & lock spaces are also provided.</p>", "", "https://cf.bstatic.com/xdata/images/hotel/max1280x900/408472073.jpg?k=8c727440849a10fb60ee9d46e4853fa5ccd573a65e67f26ececbc0211eda1d85&o=&hp=1"),
                    new HostelRoom(4, 2, "Shared Dorms", "<p>Hoi An inspired shared rooms come with fully equipped bathrooms and brand new extra comfy beds for maximum rest when you most need it. Reading lights, charging dock & lock spaces are also provided.</p>", "", "https://cf.bstatic.com/xdata/images/hotel/max1280x900/408321158.jpg?k=6c595f92c6aa40181ba9b04bfae30abe5614363366a0c9cc5b9b3d48f36a6aff&o=&hp=1"),
                    new HostelRoom(5, 2, "Twin private rooms", "<p>Hoi An inspired shared rooms come with fully equipped bathrooms and brand new extra comfy beds for maximum rest when you most need it. Reading lights, charging dock & lock spaces are also provided.</p>", "", "https://cf.bstatic.com/xdata/images/hotel/max1280x900/408472105.jpg?k=610d7f59a8ab25e54c7a18daa5e4094cffd598221500e172708af48d8311ae8d&o=&hp=1"),
                    new HostelRoom(6, 2, "Double private rooms", "<p>Hoi An inspired shared rooms come with fully equipped bathrooms and brand new extra comfy beds for maximum rest when you most need it. Reading lights, charging dock & lock spaces are also provided.</p>", "", "https://cf.bstatic.com/xdata/images/hotel/max1280x900/408472073.jpg?k=8c727440849a10fb60ee9d46e4853fa5ccd573a65e67f26ececbc0211eda1d85&o=&hp=1"),
                    new HostelRoom(7, 3, "Shared Dorms", "<p>Hoi An inspired shared rooms come with fully equipped bathrooms and brand new extra comfy beds for maximum rest when you most need it. Reading lights, charging dock & lock spaces are also provided.</p>", "", "https://cf.bstatic.com/xdata/images/hotel/max1280x900/408321158.jpg?k=6c595f92c6aa40181ba9b04bfae30abe5614363366a0c9cc5b9b3d48f36a6aff&o=&hp=1"),
                    new HostelRoom(8, 3, "Twin private rooms", "<p>Hoi An inspired shared rooms come with fully equipped bathrooms and brand new extra comfy beds for maximum rest when you most need it. Reading lights, charging dock & lock spaces are also provided.</p>", "", "https://cf.bstatic.com/xdata/images/hotel/max1280x900/408472105.jpg?k=610d7f59a8ab25e54c7a18daa5e4094cffd598221500e172708af48d8311ae8d&o=&hp=1"),
                    new HostelRoom(9, 3, "Double private rooms", "<p>Hoi An inspired shared rooms come with fully equipped bathrooms and brand new extra comfy beds for maximum rest when you most need it. Reading lights, charging dock & lock spaces are also provided.</p>", "", "https://cf.bstatic.com/xdata/images/hotel/max1280x900/408472073.jpg?k=8c727440849a10fb60ee9d46e4853fa5ccd573a65e67f26ececbc0211eda1d85&o=&hp=1"),
                };

                if (hostelId.HasValue)
                    hostelRooms = hostelRooms.Where(x => x.HostelId == hostelId.Value).ToList();
            });

            return hostelRooms;
        }

        public async Task<List<Tour>> GetTours(int? hostelId = null)
        {
            var tours = new List<Tour>();
            await Task.Run(() =>
            {
                tours = new List<Tour>()
                {
                    new Tour(1, "Riding Bamboo Boats",
                    "<p>Ride Bamboo Boats with your fellow fisherman. Have a spin in these famous basket boats and then crack open a refreshing coconut after!</p><p>A slice of traditional life in Hoi An.</p><p>Includes: Guide and boat riding.</p>",
                    "Have a spin in these famous basket boats and then crack open a refreshing coconut after!",
                    "https://images.unsplash.com/photo-1480742440191-ab4ea2aac8e7?ixlib=rb-4.0.3&ixid=MnwxMjA3fDB8MHxwaG90by1wYWdlfHx8fGVufDB8fHx8&auto=format&fit=crop&w=3538&q=80",
                    "1-1.5 hours", "$10 Per Person",
                    null, null, new List<int>() { 1, 2}),
                    new Tour(2, "FUSE Hostels Bicycle Tour",
                    "<p>A must do tour in Hoi An. You will check out the hidden alleys of the old town and surrounding area.</p>",
                    "A must do tour in Hoi An. You will check out the hidden alleys of the old town and surrounding area.",
                    "https://images.unsplash.com/photo-1504699439244-a7e34870c35d?ixlib=rb-4.0.3&ixid=MnwxMjA3fDB8MHxwaG90by1wYWdlfHx8fGVufDB8fHx8&auto=format&fit=crop&w=3540&q=80",
                    "1-1.5 hours", "$10 Per Person",
                    null, null, new List<int>() { 1, 2}),
                    new Tour(3, "Hoi An Cooking Class",
                    "<p>Immerse yourself in the famous Hoi An food scene and visit the colorful local markets.</p>",
                    "Immerse yourself in the famous Hoi An food scene and visit the colorful local markets.",
                    "https://images.unsplash.com/photo-1509813685-e7f0e4eaf3ee?ixlib=rb-4.0.3&ixid=MnwxMjA3fDB8MHxwaG90by1wYWdlfHx8fGVufDB8fHx8&auto=format&fit=crop&w=1535&q=80",
                    "1-1.5 hours", "$10 Per Person",
                    null, null, new List<int>() { 1, 2}),
                    new Tour(4, "Hai Van Pass Jeep Tour",
                    "<p>Top Gear made it famous and now you get to do it in style in a famous old fashioned army jeep.</p>",
                    "Top Gear made it famous and now you get to do it in style in a famous old fashioned army jeep.",
                    "https://images.unsplash.com/photo-1528195135899-cf5d190bb3a1?ixlib=rb-4.0.3&ixid=MnwxMjA3fDB8MHxwaG90by1wYWdlfHx8fGVufDB8fHx8&auto=format&fit=crop&w=3474&q=80",
                    "1-1.5 hours", "$10 Per Person",
                    null, null, new List<int>() { 1, 2})
                };

                if (hostelId.HasValue)
                    tours = tours.Where(x => x.HostelIds.Contains(hostelId.Value)).ToList();
            });

            return tours;
        }

        public async Task<List<Event>> GetEvents()
        {
            var events = new List<Event>();
            await Task.Run(() =>
            {
                events = new List<Event>()
                {
                    new Event(1, "Happy Hour",
                    "Every Thursday 5-7pm @ Barefoot Beach Bar",
                    "Every Thursday 5-7pm @ Barefoot Beach Bar",
                    "https://images.unsplash.com/photo-1585975776023-29a0dbc51407?ixlib=rb-4.0.3&ixid=MnwxMjA3fDB8MHxwaG90by1wYWdlfHx8fGVufDB8fHx8&auto=format&fit=crop&w=3142&q=80",
                    "209, 172, 0"),
                    new Event(2, "Free Night",
                    "Book 5 nights and get the 6th night free",
                    "Book 5 nights and get the 6th night free",
                    "https://images.unsplash.com/photo-1522771739844-6a9f6d5f14af?ixlib=rb-4.0.3&ixid=MnwxMjA3fDB8MHxwaG90by1wYWdlfHx8fGVufDB8fHx8&auto=format&fit=crop&w=3542&q=80",
                    "251, 98, 246"),
                    new Event(3, "Pool Party",
                    "Every Friday 2-11pm FUSE Beachside",
                    "Every Friday 2-11pm FUSE Beachside",
                    "https://images.unsplash.com/photo-1581610489881-f316ffcf0424?ixlib=rb-4.0.3&ixid=MnwxMjA3fDB8MHxwaG90by1wYWdlfHx8fGVufDB8fHx8&auto=format&fit=crop&w=3425&q=80",
                    "255, 102, 102"),
                    new Event(4, "Happy Hour",
                    "Every Thursday 5-7pm @ Barefoot Beach Bar",
                    "Every Thursday 5-7pm @ Barefoot Beach Bar",
                    "https://images.unsplash.com/photo-1585975776023-29a0dbc51407?ixlib=rb-4.0.3&ixid=MnwxMjA3fDB8MHxwaG90by1wYWdlfHx8fGVufDB8fHx8&auto=format&fit=crop&w=3142&q=80",
                    "209, 172, 0"),
                };
            });

            return events;
        }

        public async Task<List<Image>> GetHomePageCarouselImages()
        {
            var images = new List<Image>();

            await Task.Run(() =>
            {
                images = new List<Image>()
                {
                    new Image(1, "https://images.unsplash.com/photo-1532635241-17e820acc59f?ixlib=rb-4.0.3&ixid=MnwxMjA3fDB8MHxwaG90by1wYWdlfHx8fGVufDB8fHx8&auto=format&fit=crop&w=3036&q=80", "HOSTEL<br/>EXPERIENCE", "THE ULTIMATE", "BEGINS AT FUSE")
                };
            });

            return images;
        }
    }
}


