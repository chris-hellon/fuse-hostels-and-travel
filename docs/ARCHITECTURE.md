# Fuse architecture (.NET 10)

A ground-up refactor of the .NET 8 Fuse solution (see `legacy-src/`, gitignored, for the
original). Same product, same database, same multi-tenant model (Finbuckle, one tenant: Fuse);
different shape.

## Goals

- **No service layer.** Components send request contracts straight to a dispatcher. There are no
  `I*Service` pass-throughs, no `BaseService`, no `IFuseMediator`.
- **One contract, every render mode.** The same `Api.Send(new SearchTours(...))` call runs
  in-process during prerendering and over HTTP from WebAssembly.
- **Vertical slices.** Everything for one use case sits together: request + DTOs in `Contracts`,
  handler + validator in `Application`.
- **Handlers query EF Core directly.** They use LINQ projections, with no generic repository and no
  specification classes. Dapper and stored procedures are fine where performance needs them.
- **Explicit access control.** Every contract declares who may call it, and startup fails if one
  doesn't.

## Projects

```
src/
  Fuse.Shared          Enums, permissions, roles, claim types, constants. No dependencies.
  Fuse.Contracts       Requests, DTOs, API attributes, IApi, exceptions. Browser-safe.
                            -> Shared, Mediator.Abstractions
  Fuse.Domain          Entities, domain events, domain rules. -> Shared, Mediator.Abstractions
  Fuse.Application     Feature handlers, validators, pipeline behaviours, ports (IAppDbContext,
                            IMailer, IFileStore, ...). -> Contracts, Domain, EF Core (abstractions only)
  Fuse.Infrastructure  EF DbContext + configurations, Identity, Finbuckle, Stripe, mail +
                            Razor email templates, blob storage, Hangfire, Cloudbeds, Excel.
  Fuse.Migrations      EF migrations (SQL Server).
  Web/
    Fuse.Web           The one ASP.NET Core host: App.razor, auto-mapped API endpoints,
                            auth endpoints, Stripe webhook, legacy /api (guest app), sitemap,
                            robots, SEO redirects, Hangfire dashboard. Mediator source generator.
    Fuse.Web.Client    WebAssembly entry point: Routes, lazy-loading of Admin, HttpApi,
                            auth state.
    Fuse.Site          RCL: the public site (pages, layout, sign-in modal, basket UI).
    Fuse.Admin         RCL: the back office (MudBlazor). Lazy-loaded in WebAssembly.
    Fuse.UI            RCL: components used by both.
tests/
```

Dependency rule: `Contracts` and `Shared` must never reference anything server-side (EF, ASP.NET
Core server, Stripe, Finbuckle). They ship to the browser.

## Hosting and render mode

- **One host** serves the public site at `/` and the admin at `/admin`.
- **Global `InteractiveWebAssembly` with prerendering, except the admin.**
  - Public pages arrive as HTML, then hydrate (search engines see the full page).
  - Admin pages aren't prerendered: `Routes.razor` renders a loading screen on the server and the page renders once, in
    the browser. Prerendering them meant every admin page rendered twice, called its APIs twice, and flashed when the
    browser's copy replaced the server's (admin pages don't carry their data over with `[PersistentState]`).
  - No SignalR circuits: no Azure SignalR, no "reconnecting", no circuit-scoped identity or
    tenant problems.
  - Auto can be enabled later. Nothing below depends on WebAssembly-only behaviour.
- `App.razor` (static root) picks the `<head>` assets by path: `/admin` gets MudBlazor, everything
  else gets Fuse's CSS.
- Moving between `/` and `/admin` is always a full load.
- **Admin pages are lazy-loaded.** `Fuse.Admin.wasm` is a `BlazorWebAssemblyLazyLoad` item,
  loaded by `Routes.razor` `OnNavigateAsync` on the first `/admin` navigation.
  - Never name an Admin type in `Fuse.Web.Client`; the browser would try to load the
    assembly at startup. Refer to it by name.
  - MudBlazor itself loads eagerly: its services must be registered when the app starts. Trimmed,
    it is 232 KB (Brotli), about 7% of the roughly 3.3 MB first visit (measured on a Release
    publish, 2026-09-24).
  - Admin-only libraries that don't need DI registration at startup (charts, calendar) should
    also go on the lazy list.
- **Prerender data** (public site) is carried to the client with `[PersistentState]` so pages don't fetch twice.

## Request pipeline

```csharp
// Contracts/Tours/SearchTours.cs
[Post("tours/search")]
[Permission(FuseAction.Search, FuseResource.Tours)]
public sealed record SearchTours : PagedQuery, IQuery<PagedResult<TourSummary>> { ... }

// Application/Features/Tours/SearchTours.cs
internal sealed class SearchToursHandler(IAppDbContext db)
    : IQueryHandler<SearchTours, PagedResult<TourSummary>> { ... }

// Any component
@inject IApi Api
var page = await Api.Send(new SearchTours { Keyword = k });
```

- **Messages** are Mediator (martinothamar, source-generated) `IQuery<T>`, `ICommand<T>` or
  `ICommand`.
- **`IApi`** (in Contracts) is the only thing components use.
  - Server implementation: `MediatorApi`, which runs in-process (prerender, jobs, webhooks).
    **Each call gets its own DI scope and DbContext**, like an HTTP request. Prerendering loads
    components in parallel, and a shared DbContext throws on concurrent use.
  - Browser implementation: `HttpApi`, which reads the contract's route attribute and calls the
    endpoint.
  - Errors surface as the same exception types either way. The server writes ProblemDetails with an
    `errorType` extension; `HttpApi` rethrows `NotFoundException`,
    `RequestValidationException`, `ConflictException`, `BusinessRuleException` and so on.
- **Endpoints** are generated at startup: every contract with a route attribute is mapped under
  `/api/app/{route}`.
  - GET/DELETE: the request is built from route values and the query string.
  - POST/PUT: the request is the JSON body; route values must match.
- **Access attributes.** Exactly one is required per contract:
  - `[Permission(action, resource)]`: staff permission (DB-backed, cached).
  - `[Authenticated]`: any signed-in user (for example a guest reading their own bookings). The
    handler must scope the data to the current user.
  - `[Public]`: anonymous (public site content, basket pricing, sign-in).
- **Access is enforced twice:**
  - On the endpoint (401/403).
  - In the `AuthorizationBehaviour` pipeline step, so in-process calls (prerender, jobs) get the
    same checks.
- **Pipeline order:** Logging → Authorization → Validation (FluentValidation) → Handler.
- **CSRF:** the auth cookie is `SameSite=Lax`. `/api/app` rejects non-GET requests that lack the
  `X-Fuse-Api` header, which cross-site forms can't send.

## Gotchas found while building the foundation

- **Layouts.** `@layout` and `[Authorize]` go in `Pages/_Imports.razor`, never the project root.
  A root `_Imports` also applies to the layout itself, which then nests forever: 100% CPU and no
  response.
- **Projections must use object initializers** (`new Dto { Name = x.Name }`), never positional
  record constructors, whenever the result is sorted, filtered or paged afterwards. EF Core can't
  translate member access on a constructor-built object: it fails at runtime, not compile time.
- **Test every admin page by loading it directly** (the prerender tests check each serves its loading screen), not only by
  navigating in the browser. In-app navigation never exercises the in-process path.
- **Status code pages** are skipped for `/api`. Otherwise a 401/404 from a POST is re-executed as
  a POST to `/not-found`, and Blazor answers 400 "incorrect Content-type".
- **`IExceptionHandler` sees the rewritten error path.** Read the original from
  `IExceptionHandlerPathFeature`.

## Auth

- **One ASP.NET Identity cookie** covers both apps. Staff and guests are the same user store,
  separated by roles.
- **Sign-in, register and sign-out** are HTTP endpoints (`/api/auth/*`) called by `HttpClient` from
  WebAssembly. The Fuse modal lives in the layout, signs in without a page load, then refreshes the
  `AuthenticationStateProvider` from `/api/auth/me`.
- **Initial auth state** comes from `AddAuthenticationStateSerialization`.
- **`/api/auth/me`** returns the claims plus effective permissions. Client-side `AuthorizeView`
  and policies use them for display only; the server re-checks everything.
- **Legacy `/api/*`** (JWT, client-credentials, used by the Fuse guest app) keeps its routes and
  shapes. It is reimplemented as thin endpoints over the new handlers.

## Data

- **`IAppDbContext`** (Application) exposes `DbSet<>`s plus `SaveChangesAsync`. It is implemented
  by `AppDbContext` (Infrastructure), a Finbuckle multi-tenant Identity DbContext.
- **Schema compatibility.** Entity and table names stay compatible with the existing database, and
  the first migration is a baseline over the current schema. Domain clean-ups that change the
  schema get their own migrations. Never `migrations remove`.
- **Schema parity is proven, not assumed.** Scaffolding a migration against the carried-over
  snapshot must produce an empty `Up()`. Finbuckle 6's conventions (`TenantId nvarchar(64)`,
  the surrogate `UserLogins.Id`, tenant `Id nvarchar(64)`) are reproduced in
  `AppDbContext.KeepLegacyTenancyConventions` and `TenantDbContext`. Identity stays on schema
  version 1 (no passkeys table).
- **Migrations:** the 60 legacy migrations keep their IDs, so `__EFMigrationsHistory` in existing
  databases lines up. Use the local tool:
  `dotnet tool run dotnet-ef migrations add <Name> --project src/Fuse.Migrations --startup-project src/Fuse.Migrations --context AppDbContext --output-dir Application`.
  To undo a scaffold, delete its files and `git checkout` the snapshot. Never run
  `migrations remove`.
- **Domain events** are raised on entities and published through Mediator after `SaveChanges`.
- **Auditing** stays in the DbContext.
- **Tenant.** A single tenant resolved by the static strategy. Tenant `Id` is `"fuse"`; its
  identifier is `"fusehostelsandtravel"`. Tenant-scoped queries use `Id`.

## Tests

`dotnet test --project tests/Fuse.Web.Tests` runs the real app against a throwaway database
(`travaloud_tests_<guid>`) on the local SQL Server. The database is built from the EF model plus the table
types, computed columns and stored procedures copied from the local dev database, and dropped at the end. The dev database is
never touched. The tests are generated from the contracts, so every new endpoint is covered
automatically:

- Every contract has a route, exactly one access rule and a registered handler.
- Anonymous callers get 401 from every non-public contract; guests get 403 from every staff one.
- Every paged search succeeds, as an admin, sorted by each column of its row type in both
  directions. This catches queries that only break once sorted.
- Every table type and computed column copies from the dev database.
- Auth behaviours (generic credentials message, staff-only sign-in, permission claims, duplicate
  registration).

## The real database vs the EF model

The legacy database was tuned by hand: indexes, table types, a computed column, and tightened
column types. It had drifted from the EF model in ways a snapshot comparison can't see.

- **Stored procedures, table types, computed columns and the 39 performance indexes live in the database only**
  (no `.sql` files in the project): change them there and the app uses them.
- **Column types** changed by hand are mirrored in `HandTunedColumns`, acknowledged by the empty
  migration `AlignModelWithDatabase`.
- `docs/check-model-drift.sh` compares any database with the model, column by column. It is
  clean against dev (2026-09-24). **Run it against production before the first deploy.**
- **Legacy is still being developed.** Changes there (e.g. `AddEventExternalUrl`, 2026-09-24)
  must be brought across: refresh `legacy-src`, then port the entity, migration and snapshot.
  The migration keeps its original ID.

## Other decisions

- **Email templates** are Razor components rendered with the built-in `HtmlRenderer`. This
  replaces BlazorTemplater, RazorEngineCore and the ClearScript/V8 runtime.
- **SCSS** is compiled at build time. No WebOptimizer, no runtime V8.
- **The basket** is client state (browser storage, `BasketState` service). The server re-prices and
  validates at checkout (`PriceBasket`, `Checkout`). There is no server session.
- **The Fuse UI** is rebuilt as Blazor components but must look identical to the current site. It
  keeps the existing SCSS/MDB styles; jQuery/MDB JavaScript widgets are replaced by components
  (with small JS interop only where unavoidable).
- **File storage** is `IFileStore`: Azure Blob Storage whenever legacy's `AzureStorageSettings:ConnectionString`
  is set (appsettings.json, every environment, as legacy); without it (tests) a local folder
  (`{ContentRoot}/.uploads`, served at `/uploads`). URLs keep the legacy shape
  `{cdn}/{tenant}/assets/images/{guid}{ext}`, with `?w=1000` / `?w=700` width hints (`ImageUrls`).
- **Files:**
  - Uploads: `FileUpload` (name, content type, base64) in contracts.
  - Downloads: handlers return `FileDownload`; the client saves it via a small JS helper.
- **Packages** are on current majors: Mediator 3, Finbuckle 10, FluentValidation 12, MudBlazor 9,
  Stripe 52, Hangfire 1.8. MediatR is gone.

## Payments, email and background jobs

Nothing outside production can take real money, email a real person or call a partner:

- **Stripe** (`IPaymentGateway`, Infrastructure/Payments): keys come from configuration (the appsettings files, which aren't in git; never
  source). Startup refuses a live key unless `Stripe:AllowLiveKeys` is true (production only). With no key, every
  card call refuses with a clear message. Checkout returns to `{Stripe:SiteUrl}/travaloud-payment-confirmation/{id}`,
  whose page sends `ConfirmCheckout`; Stripe's webhook (`/api/stripe/webhook`, off until `Stripe:WebhookSecret` is
  set) sends the same command. It's idempotent: the payment is recorded once, emails go once.
- **Email** (`IEmailSender`, Infrastructure/Mail): `Mail:Mode` is `Pickup` by default, so messages are written as
  .eml files to `{content root}/.mail` (gitignored). `Smtp` sends for real, but outside production only to
  `Mail:AllowedRecipients`. Templates are Razor components rendered with `HtmlRenderer` (Infrastructure/Mail/Templates),
  branded from `Mail:Brand`. Handlers load an `EmailModel` first: templates get no database.
- **Background jobs** (`IBackgroundJobs`, Infrastructure/Jobs): Hangfire, in its own schema (`Hangfire:Schema`,
  default `HangfireRebuild`). On the live database legacy's Hangfire uses the `HangFire` schema; sharing it would let
  each app's server run the other's jobs. Jobs are stored as a call on a service with its arguments, so pass plain
  data (emails are stored with array lists). Jobs get no tenant or user: render or load what they need first.
  - **Who runs jobs:** every instance can queue and show the dashboard (`/admin/jobs`, "View Hangfire" permission);
    only production, or an instance on a local database, runs them (`Hangfire:Server` overrides), so a staging or
    developer copy pointed at the live database never works production's queue.
  - **Scheduled jobs** (Web/Jobs/RecurringJobs.cs): tour manifest emails (as legacy: at midnight each of today's
    TourDates rows is scheduled per location of its tour, for 1 hour before it leaves, to that location's hostels),
    Cloudbeds guest import (as legacy, at :00 and :30). Each has a `Jobs:{Name}` switch. **Cutover:** this app
    replaces legacy on the same server; its jobs live in the `HangfireRebuild` schema, so legacy's recurring jobs
    (in `HangFire`) simply stop running with legacy.
  - Tests set `Hangfire:Enabled=false`: queued jobs run inline, delayed ones (`Schedule`) are only recorded,
    and recurring ones are called directly.
- **Partner webhooks** (`IPaidBookingWebhook`): each `ApiSettings:Clients` entry's `WebhookUrl` (legacy's config, empty
  today); production only unless `ApiSettings:AllowOutsideProduction`.
- Tests replace all of these with fakes (tests/…/TestDoubles.cs): no test ever reaches Stripe, SMTP or a webhook.

## Public booking API (legacy /api)

Legacy's machine-to-machine booking API, kept exactly (the Fuse guest app and partners call it): same routes, JSON,
status codes, error bodies and docs. Separate from the admin's own `/api/app/...` endpoints.

- **Routes**: `POST /api/token` (client credentials → JWT, 10/min per IP), then with `Authorization: Bearer` from a
  staff user (120/min per client): `/api/users` (search, get, create, update), `/api/tours` (categories, locations,
  search, `{id}` departures), `/api/packages` (search, `{id}`, match), `/api/pack-incentives` (list, match),
  `/api/bookings` (search, `{id}`, create, `{id}/flag-as-paid`, `{id}/stripe-checkout`). Web/PublicApi maps them;
  Application/Features/PublicApi does the work (legacy's response models live there, field for field).
- **Docs**: `/api/docs` is legacy's Scalar page over **legacy's own OpenAPI document** (`PublicApi/openapi-v1.json`,
  embedded, served at `/api/openapi/v1.json`). Change an endpoint's shape and that document must change with it.
  The admin's development-only spec moved to `/api/app/openapi/v1.json`.
- **Config**: `ApiSettings` as legacy (`Jwt:Key/Issuer/Audience/TokenLifetimeMinutes`, `Clients[]` with `ClientId`,
  `ClientSecret`, `UserId` (the service staff user), `DevUserId`, `WebhookUrl`). Limits: `RateLimits:ApiTokenPerMinute`,
  `RateLimits:ApiPerMinute`.
- **Test bookings** (`isDevelopment`): tagged `[TEST]`, credited to the client's `DevUserId`, and their checkout uses
  Stripe **test mode** even in production (`Stripe:TestSecretKey`, and `Stripe:TestWebhookSecret` for their webhooks).
- **Stripe webhook**: legacy's `/webhooks/stripe-payment` is served alongside `/api/stripe/webhook`.

## Admin UI conventions

- **Folders.** A feature folder under `Pages/` holds its list page and wizard at the top, with
  `Steps/`, `Dialogs/` and other sub-areas in folders (see `Pages/Tours`). A feature's own
  `_Imports.razor` adds `@using`s for its subfolders. Shared pieces live in `Components/`:
  `Tables/` (EntityTable), `Forms/` (RichText, StepToolbar, AddMenu, ViewSwitch), `Images/`
  (ImagePicker, ImageGallery), `Wizard/`, `Layout/`, `Users/`.
- **Injects.** `IApi Api`, `ISnackbar Snackbar` and `IDialogService Dialogs` are injected for every
  admin component in `_Imports.razor`; don't re-declare them. Anything else is `@inject`ed locally.
- **Reuse before building.** List pages use `EntityTable`; wizard steps with actions use
  `StepToolbar` (title + summary, one `AddMenu`, optional `ViewSwitch`, destructive actions in the
  ⋮ menu); step tables use `Class="admin-subtable"` with icon-button row actions; images use
  `ImagePicker` / `ImageGallery` (they handle empty and broken images).
- **Wizards save once.** A wizard edits a working copy (e.g. `Pages/Tours/TourDraft.cs`) and
  Save sends every change in one command handled in one transaction (`CreateTour`/`UpdateTour`
  carrying `TourChanges`, applied by `TourChangeSet`). Nothing is written while the user moves
  between steps; Cancel asks before discarding. Images are the one exception to "nothing is sent":
  they upload first (`UploadTourImage`) to keep Save small, but are only attached on Save, and the
  server only accepts URLs its own file store issued (`IFileStore.IsTenantFile(url, FileKind)`).
- **Saving shows on the Save button** ("Saving…" with a spinner); the dialog's content is locked
  and faded (`inert`), never covered by a dark overlay. `ModalWizard` does this for every wizard.
- **Confirmations** use `Dialogs.ConfirmAsync(...)` (`Components/Dialogs`): the title asks the
  question ("Delete this price?"), the message says exactly what happens and when, and the buttons
  name both outcomes ("Keep price" / "Delete price"; destructive ones are red). Never "OK"/"Yes".
  Steps whose edits wait for the parent's Save show `<SavedWithParentNote Parent="tour" />`.
- **No flicker.** Load everything a dialog needs before showing its content (in parallel), render
  the dialog once (loading state inside it). Wizard dialogs size to their content and stay centred (legacy).
- **Rich text fields always go in a "Content" step** built with `ContentSections` (Components/Forms):
  a section list with filled/empty/required markers, one editor that fills the space with a fixed
  toolbar, and Expand for a near full-screen editor. Never tabs, and never rich text mixed in with
  ordinary fields. This applies to every entity with rich text (tours, locations, and everything
  still to port: properties, events, pages, travel guides, job vacancies...). Define the sections
  as `ContentSection(label, hint, get, set, Required)` once (not per render). `RichText` flushes its
  pending (debounced) change when it closes, so switching fast never drops text. It's responsive by
  its own width (container query): under 640px the sections become a row of pills above the editor.
- **A few basic fields go in the Content sidebar, images in their own last step.** When a record
  is mostly text (locations, tour categories, travel guides), its name/title fields are a
  `ContentSection.Fields("Details", ...)` section at the top of the Content step (ordinary fields shown
  instead of the editor), and the banner/slideshow are an Image(s) step at the end. `ContentSections.
  EnsureRequired()` on leaving the step jumps to the first unfinished required section (fields in a
  section aren't on screen, so the step's form can't check them). Records with many fields (tours,
  events, packages) keep a Details step and put images last.
- **Switches in a row of fields** use `Class="switch-cell"` on their MudItem so they line up with the
  field boxes (not centred on box + helper text).
- **Small wizards stay small.** Don't give one or two fields a step of their own: put a single image
  next to the fields in a "Details" step (fields left, ImagePicker right), then a Content step.
  When Details already has many fields (e.g. packages), the image gets its own last step instead of
  squeezing the fields into half the width. Galleries always get their own step. Steps have no minimum height.
- **Sizing fields in a row:** put the sizes on a wrapping grid (`.tier-row`, `.field-row`), never a
  `flex` in a Mud field's `Style` (it lands on an inner column-flex element and becomes a height).
- **Pictures inside rich text** are uploaded, never inlined as base64: give `ContentSections` (or
  `RichText`) an `UploadImage` (e.g. `UploadTravelGuideImage`) and the editor's image button stores
  the file and inserts its URL. Without it Quill falls back to base64, so pass one wherever authors
  add pictures to long text (travel guides; legacy only allowed it there).
- **SEO fields:** `SeoFieldsEditor` takes `IMetaFields` (slug, keywords, description) and shows the
  page title, headings and scripts too when the input is a full `ISeoFields`.
- **Page cards** (profile pages): `MudPaper Class="admin-card"`, a `.admin-card__head` (title and
  caption) and `.admin-card__actions` (note on the left, button on the right). Cards in one row
  share their height. `AdminPageHeader` is a flex row, never a MudToolBar (fixed height).
- **Text-heavy records with nothing else** (job vacancies, page modals) use `ContentDialog`: one
  screen with the Content sidebar (a Details fields section + the rich text) and Save/Cancel.
  Records that also need images or SEO use a ModalWizard.
- **Tiny forms are plain dialogs.** A few plain fields with no image or rich text (e.g. an event
  category: name, colour, order) use a `MudDialog` with a `MudForm`, not a one-step wizard.
- **Rules the wizard previews live in `Fuse.Shared`,** so the admin and the server can't
  disagree: `TourDurations` (departure end times), `EventRecurrence` (event dates from a repeat rule,
  also its plain-English summary), `EventColours` (legacy "r, g, b" event colours <-> hex).
- **Wizard headers fit themselves.** `ModalWizard` shows `WizardSteps` in the title row: full titles,
  then numbers plus the current title, then "Step 3 of 7" with a progress bar on its own line,
  chosen by measuring (wizard-steps.js), not breakpoints. Don't add fixed-position step lists.
- **Naming: "Location", never "Destination",** everywhere except the domain entities, their DbSets and
  the database (which keep legacy names). Contracts, handlers, routes, URLs and UI say Location.
- **Table filters go in `AdvancedSearchContent`** (legacy "Advanced Search": a panel above the table
  holding the search box and the filters; `ExpandAdvancedSearch` opens it, as on the Manifest).
  Don't squeeze filters into the table toolbar.
- **Tables check permissions themselves:** give `EntityTable` a `Resource` and plain callbacks
  (`OnCreate="CreateAsync"`); it shows Create / Edit / Delete only with that permission (legacy did the same).
- **Gotcha: string parameters need `@`.** `Banner="_model.ImagePath"` passes the literal text
  "_model.ImagePath" to a `string` parameter (non-string parameters are evaluated). Write `@_model.ImagePath`.
- **Gotcha: a menu stays open until its item's `OnClick` finishes.** An item that opens a dialog and
  awaits it leaves the menu open behind the dialog. Wrap such actions in
  `MenuActions.AfterMenuCloses(action, DispatchExceptionAsync)` (EntityTable does this for Edit/Delete).
- **Gotcha: MudBlazor 9 menu activators.** A custom `<ActivatorContent Context="menu">` must open
  the menu itself: `OnClick="@(e => menu.ToggleAsync(e))"`. Without it the menu never opens.
- **Gotcha: validators are found by assembly scan** and built by DI, so every `AbstractValidator`
  must take only services. Share rules through a static helper instead (see
  `TravelGuideUniqueness`). The test host validates the container on build, so a bad one fails
  every test.
- **Gotcha: don't build or run tests while the dev server runs.** The WASM files change under it and
  the browser caches 404s for the new ones (the admin never becomes interactive). Stop the server,
  build, then `dotnet run --no-build`; if a browser already cached the 404s, re-fetch them with
  `fetch(url, {cache: 'reload'})`.
- Unknown component tags (RZ10012) fail the build (`Directory.Build.props`): they would
  otherwise render nothing.

## Public site (Fuse) conventions

- **Folders** (`src/Web/Fuse.Site`):
  - `Layout/`: `FuseLayout`, the header, footer and cookie banner, and `ISiteDialogs` (the layout's API).
  - `Pages/{Feature}/`: a page and its section components (e.g. `Pages/Home/HomeHero.razor`).
  - `Components/{Banners,Booking,Cards,Dialogs,Forms,Sections,Seo}/`: pieces shared between pages (e.g.
    `HeaderBanner`, `NavPills`, `RoundedImageSection`, `SeoHead` with `PageName` for "Name - Fuse" titles and breadcrumbs).
  - `Interop/FuseJs.cs` wraps `wwwroot/js/fuse.js`; `Media/CdnImages.cs` sizes image URLs.
  - `Styles/`: the SCSS (see below).
- **It must look identical to the live site.** The legacy MDB5 Pro SCSS is copied into `Styles/mdb` and
  `Styles/legacy`, and compiled at build time by AspNetCore.SassCompiler (`sasscompiler.json`) into
  `wwwroot/css/fuse.css`, which is git-ignored. Rebuild-only styles go in `Styles/components`.
  - `SassCompilerEnableWatcher` must stay false. Otherwise the package stamps an attribute from its own
    assembly onto Fuse.Site, and WebAssembly fails to start on every page, admin included.
- **Widgets output the markup legacy's scripts produced** (MDB select, form outlines, Owl carousel dots),
  so the theme styles them unchanged. Exception: the check in / out picker (`DateRangePicker`) is a new design
  the user asked for (`Styles/components/_date-range-picker.scss`); legacy's daterangepicker styles are gone.
  - Blazor owns that markup. `fuse.js` only measures, animates and reports back: scroll thresholds,
    reveals, the marquee, staggered cards, carousel pages.
  - Never add or remove classes from script on elements Blazor renders dynamically.
- **The layout loads the header and footer data once** (`GetSiteChrome`, `[PersistentState]`) and cascades
  it. It also hosts the modals and the date picker, so they sit above every page's stacking context.
- **Keep the site small: build from shared pieces.** Before writing markup, look for an existing component;
  when two pages share a pattern, extract it rather than copy it. Repeated blocks inside a page are data plus a
  loop (e.g. Our Story's core values).
  - Pages inherit `SitePage` (`Pages/SitePage.cs`): the CMS record (`SitePages` ids) loads and persists
    automatically. Override `PageId` and `LoadAsync` for extra data, marked `[PersistentState]`.
  - Dialogs are `FuseModal` wrapping a form component (`SignInForm`, `BookNowForm`). The modal creates its
    content on each open, so keep a form's state in the form, never in the modal's host.
  - Components that call `fuse.js` inherit `JsComponent`: `Js` and clean-up (`OnDisposeAsync`) are handled.
  - Data comes from small public queries (`ListSiteHostels`, `ListSiteTours`, `ListSiteEvents`, `GetSitePage`)
    that pages combine, never one query per page.
  - Shared markup: `HeaderBanner` (`Intro`, `Ovals`, `Images` for a `Slideshow`), `SectionIntro`, `NavPills`,
    `RoundedImageSection` (image or `VideoSrc` via `CircleVideo`), `HostelRows`, `RoomCards` (with its room
    lightbox), `FeaturesTable`, `PillTabs`, `ToursCarousel`, `WhatsOn` (events poster + line-up), `HostelDetails`, `ShareButton`, `BookNowForm` (`BookNowStyle`
    Hero / Banner / Modal, optional `HostelId`), `BookNowBanner`, `OutlineInput`, `FuseSelect`, `Validation`,
    `FormNotch`. `FuseModal` takes other classes and an outside close button for lightboxes.
  - `EventsCalendar` is legacy's FullCalendar (loaded from jsDelivr by `fuse.js mountCalendar`); Blazor feeds
    each range from `ListSiteEventOccurrences` and shows the event card. Event dates are expanded once, in
    `Features/Events/EventOccurrences` (admin and site). `/events/calendar.ics` is the iCal feed.
  - Pages for a record (a hostel, a tour) load it by slug with a `GetSite…` query that throws
    `NotFoundException`, and call `NavigationManager.NotFound()` (a real 404). Their banner heading uses
    `PageHeadings.ForRecord`; `SeoHead` takes `Parent` for the three-level breadcrumb.
- **CMS-backed pages** load their record with `GetSitePage(id)` (SEO and H1-H3; `PageHeadings.Of` picks the banner
  heading as legacy did). Fixed copy stays in the page's markup.
- **After every navigation** the layout calls `fuse.js pageChanged` (pads under the white header, sizes
  circles, reveals). Page-specific effects start from the page's own `OnAfterRenderAsync`.
- **Public queries** (`Features/Site`) are `[Public]`. In Production only, they are cached in memory for an hour
  (`SiteCache`), as legacy did. Any successful command clears the cache (`SiteCacheBehaviour`), so admin edits
  show at once. Other environments always read the database.
- **SEO** (as legacy): every page is prerendered, so crawlers get the full HTML without running WebAssembly.
  `SeoHead` writes the title, meta tags, lower-case canonical and structured data. Content behind tabs or
  lightboxes is still in the HTML, hidden (PillTabs panels, RoomCards descriptions). `/robots.txt` and
  `/sitemap.xml` match legacy's (`SiteSeoEndpoints`); robots blocks everything outside Production. Page
  Management's redirects are 301s applied before routing, www goes to the bare domain, and an unknown
  address is a real 404 (legacy redirected to /error/404).
- **Analytics** (Google tag, Ads, Tag Manager) are output only in Production. Other environments send
  `noindex, nofollow`.
- **Comparing with the live site:** it is fine to read https://fusehostelsandtravel.com (read-only).
  Hide its cookie banner with a script rather than accepting it.

## Checking the admin in a browser (development)

Open `http://localhost:5104/dev/sign-in?returnUrl=/admin/...` to be signed in as a local admin
(`dev-admin@localhost.invalid`, created on first use, no password). It exists only in Debug builds,
in the Development environment, when non-local databases aren't allowed, and for requests from this
machine (`Auth/DevSignIn.cs`); a test checks it isn't there otherwise.

## Porting a feature (checklist)

1. Read the legacy request, handler, specs, DTOs and every UI usage in `legacy-src/`.
2. Contracts: one file per use case (the request and any DTOs only it uses). Shared DTOs go in
   `Contracts/<Area>/Models`. Add route + access attributes. Use records and init-only props.
3. Application: one file per use case in `Features/<Area>/`, holding the handler plus validator.
   Query `IAppDbContext` directly; project with `Select` to DTOs. No specs, no repositories.
4. Keep behaviour identical unless the change is a deliberate, noted fix.
5. Add or adjust tests for non-trivial logic (pricing, commission, availability).
6. UI: use the shared admin pieces (see "Admin UI conventions"): EntityTable for lists, ModalWizard
   for create/edit, a Content step with ContentSections for rich text, StepToolbar above tables in
   steps, ImagePicker/ImageGallery for images, Dialogs.ConfirmAsync for confirmations. Wizards save
   once, on Save.
