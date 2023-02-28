# Fuse Hostels & Travel

![Fuse Hostels & Travel banner image](https://fusehostelsandtravel.blob.core.windows.net/images/fuse-hostels-banner-home-page.webp)

https://fusehostelsandtravel.com/

A .NET Core 6 Razor Pages Web App for a Travel & Hospitality client in Vietnam taking Hostel & Tour bookings.
The client s
The client requested a platform where the user could register, select a package and purchase the package using Bitcoin or Stripe.
Upon successful payment, the user would have access to members only content, including embedded videos, live trading data and downloadable resources.

This is .NET 6 Web App using Clean Architecture and Razor Pages. It was created from the dotnet new webapp template and modified adding a custom booking engine widget, Material Design Bootstrap v5, Microsoft Identity and other packages/features.

The app inherits a base Razor Class Library application, located here https://github.com/chris-hellon/travaloud-base

Features of the Web App includes:

* A custom Content Management System
* User login, registration and subscription payment, handled consuming a Bitcoin Node Rest API client or a Stripe Rest API client
* An Azure SQL database
* An Azure CDN with Storage Containers and Blobs to serve all JS, CSS and Images
* Notification emails sent out to users whenever new content is uploaded using a Google SMTP Client

Tech Stack includes:

* .NET Core 6
* C#
* MVC
* Azure SQL
* Azure CDN
* Dapper
* RestShap API
* Rollbar Error Handling
* jQuery
* Bootstrap 5
