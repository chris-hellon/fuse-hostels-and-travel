# Fuse Hostels & Travel

![Fuse Hostels & Travel banner image](https://fusehostelsandtravel.blob.core.windows.net/images/fuse-hostels-banner-home-page.webp)

https://fusehostelsandtravel.com/

This is .NET 6 Web App implementing Clean Architecture and Razor Pages. It was created from the dotnet new webapp template and modified adding Material Design Bootstrap v5, Microsoft Identity, Azure Container/Blob Storage, Microsoft SQL Server, a Custom Booking Engine widget, and other packages/features.

The app inherits a base Razor Class Library application, located here https://github.com/chris-hellon/travaloud-base

Features of the Web App includes:

* A Booking Wdiget which integrates with a third party, on successful payment, the output is submitted to an Azure SQL database for better reporting
* User login, registration and account/booking management
* An Azure SQL database
* An Azure CDN with Storage Containers and Blobs to serve all JS, CSS and Images
* Notification emails

Tech Stack includes:

* .NET Core 6
* C#
* MVC
* Azure SQL
* Azure CDN
* Dapper
* Rollbar Error Handling
* jQuery
* Bootstrap 5
