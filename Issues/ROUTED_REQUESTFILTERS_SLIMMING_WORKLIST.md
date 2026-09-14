# Routed.RequestFilters slimming worklist

## Ziel

`Eigenverft.Routed.RequestFilters` wird auf Filterlogik und filtereigenen Zustand reduziert. Allgemeine Web-, Hosting-,
Konfigurations- und Logging-Infrastruktur wird entweder ersatzlos aus diesem Projekt entfernt oder innerhalb der
verbleibenden Filter durch ein bereits vorhandenes Capability-Paket ersetzt.

Diese Arbeitsliste konkretisiert
[`PACKAGE_CAPABILITY_MIGRATION_MAPPING.md`](PACKAGE_CAPABILITY_MIGRATION_MAPPING.md). Sie ist die Reihenfolge für die
Implementierung, keine Aufforderung, andere Repositories zu ändern.

## Feste Grenzen

- Änderungen erfolgen ausschließlich in `Eigenverft.Routed.RequestFilters`.
- `Eigenverft.Web.EdgeReverseProxy` ist die aktuelle Architekturreferenz und bleibt read-only.
- `Eigenverft.App.ReverseProxy` ist ein historischer Consumer und bleibt read-only.
- WebLib und NetLib bleiben read-only; verwendet werden veröffentlichte Capability-Pakete, keine Projektverweise.
- Ein Consumer-Paket wird nicht zu einer RequestFilters-Abhängigkeit, nur weil eine alte RequestFilters-API dorthin
  migriert werden kann.
- `FileExtensionBlocking` bleibt Filterlogik, obwohl Static-File-Serving entfernt wird.
- `TlsProtocolFiltering` bleibt Request-Filterlogik, obwohl Kestrel-/SNI-Konfiguration entfernt wird.
- SQLite bleibt, solange der SQLite-basierte Filter-Event-Speicher Bestandteil der Library ist.

## Zielbild der direkten Eigenverft-Abhängigkeiten

Nur diese fünf Pakete dürfen nach der Migration direkt von RequestFilters referenziert werden:

1. `Eigenverft.WebLib.Middleware.Primitives`
2. `Eigenverft.WebLib.ClientNetwork`
3. `Eigenverft.NetLib.Networking`
4. `Eigenverft.NetLib.Configuration.Binding`
5. `Eigenverft.NetLib.Logging.Deferred`

RequestFilters verwendet APIs aus allen fünf Paketen direkt. Dass `ClientNetwork` einen Teil davon transitiv mitbringt,
ist deshalb kein Grund, die direkten Referenzen zu verschleiern.

## Aktueller Zwischenstand

Stand: 2026-09-13 nach Abschluss der Filter-, Capability- und Storage-Verhaltensprüfung.

- Abgeschlossen: `RF-00`; die Breaking-Change-Baseline ist auf den unveränderten Ausgangscommit
  `bae9d6de66c713c49cdc83963627ea6ab7c9e22d` festgelegt und in Mapping sowie Release Notes nach API-Gruppen erfasst.
- Abgeschlossen: `RF-01` bis `RF-07`.
- Abgeschlossen: `RF-08`; das Paket zielt ausschließlich auf `net8.0` und `net10.0`, und die Änderung ist als Breaking
  Change dokumentiert.
- Abgeschlossen: `RF-09`; alle fünf benötigten Capability-Pakete sind mit veröffentlichten Versionen eingebunden.
- Abgeschlossen: `RF-10` bis `RF-12`; lokale Middleware-Primitives, Remote-IP-Kontext, CIDR-Mathematik und
  Collection-Binding sind durch
  die schmalen Capability-Pakete ersetzt.
- Abgeschlossen: `RF-13` (lokale Deferred-Logger-Kopie durch das Capability-Paket ersetzt).
- Abgeschlossen: `RF-14`; die verbleibende Pattern-Logik ist filterintern und `GenericExtensions/` ist entfernt.
- Abgeschlossen: `RF-15` und `RF-16`; Quellbaum und Paketgraph sind bereinigt, Release Notes und Paketinhalt geprüft.
- Abgeschlossen: `RF-17`; Build, Filter-/Storage-Verhalten, Paketgraph, Pack, Legacy-Suche und Repository-Grenzcheck
  sind grün.
- Letzte Verifikation: `dotnet build src/Eigenverft.Routed.RequestFilters.slnx --configuration Release` für
  `net8.0` und `net10.0`, erfolgreich mit 0 Warnungen und 0 Fehlern.
- Tests: 47 Tests laufen auf `net8.0` und `net10.0` erfolgreich. Abgedeckt sind Collection-Binding, Pattern-/Priority-
  Klassifikation, ClientNetwork/CIDR, die Registrierung und Aktivierung jedes verbleibenden Filters, Filter-Event-
  Erzeugung, EvaluationGate, DevelopmentUnlocker sowie Null-/InMemory-/SQLite-Storage.
- Durch die Pipeline-Tests behoben: alle paketbasierten DI-Prüfungen verwenden geschlossene Logger-Typen;
  `AddBrowserBootstrapFiltering()` registriert seine benötigte Data-Protection-Infrastruktur; doppelte Standard-
  Registrierung eines Filters wird durch das Paket tatsächlich unterdrückt.
- Nächster sicherer Einstieg: Gesamtänderung reviewen und bei Freigabe in logisch getrennte Commits überführen.
- Repository-Grenze: ausschließlich RequestFilters wurde verändert; WebLib, NetLib, EdgeReverseProxy und
  App.ReverseProxy blieben read-only.

## Abarbeitungsreihenfolge

Jedes Arbeitspaket soll einzeln reviewbar sein. Nach jedem Paket mindestens die Solution bauen; nach Änderungen am
Filterverhalten zusätzlich die betroffenen Filtertests ausführen.

### RF-00: Ausgangszustand festhalten

- [x] `dotnet build src/Eigenverft.Routed.RequestFilters.slnx --configuration Release` ausführen und das Ergebnis notieren.
- [x] Die aktuell veröffentlichte öffentliche API als Breaking-Change-Baseline erfassen (Ausgangscommit
  `bae9d6de66c713c49cdc83963627ea6ab7c9e22d`; entfernte/geänderte API-Gruppen sind im Mapping und in den Release Notes
  festgehalten).
- [x] Festhalten, dass derzeit kein Testprojekt in der Solution vorhanden ist.
- [x] Ein schlankes Testprojekt für Charakterisierungstests anlegen.

Abnahme:

- Der Ausgangsbuild ist reproduzierbar dokumentiert.
- Öffentliche Löschungen können später eindeutig in Release Notes und README benannt werden.

### RF-01: Static-File- und PWA/Blazor-Helfer entfernen

Aktion: löschen, kein Paket in RequestFilters hinzufügen.

- [x] `Infrastructure/UseStaticFilesWithPwaAndBlazorContentTypes.cs` entfernen.
- [x] `GenericExtensions/IApplicationBuilderExtensions/UseNonAssetFiles.cs` entfernen.
- [x] Zugehörige Namespaces, XML-Beispiele und README-Beispiele entfernen.

Capability außerhalb dieses Projekts:

- `Eigenverft.WebLib.StaticFiles`
- insbesondere dessen `UseStaticFiles(...)` und `AdditionalMappings.WebApp`

Begründung: Dateien bereitstellen und Content-Types konfigurieren ist Anwendungskomposition, keine Request-Filterlogik.
Ein Consumer, der diese Funktion benötigt, referenziert `Eigenverft.WebLib.StaticFiles` selbst.

Abnahme:

- Im RequestFilters-Projekt existieren keine Static-File-, PWA- oder Blazor-Content-Type-APIs mehr.
- `FileExtensionBlocking` bleibt unverändert vorhanden.
- Keine Referenz auf `Eigenverft.WebLib.StaticFiles` wurde hinzugefügt.

### RF-02: Kestrel, SNI und Zertifikaterstellung entfernen

Aktion: löschen, kein Paket in RequestFilters hinzufügen.

- [x] `GenericExtensions/ConfigureWebHostBuilderExtensions/ConfigureKestrelSni.cs` entfernen.
- [x] `GenericExtensions/ConfigureWebHostBuilderExtensions/ConfigureKestrelSniFromConfiguration.cs` entfernen.
- [x] `Utilities/Certificate/CertificateManager.cs` entfernen.

Capabilities außerhalb dieses Projekts:

- `Eigenverft.WebLib.Kestrel.Sni`
- intern dazu passend `Eigenverft.NetLib.Security.Certificates`

Begründung: Serverendpunkte, TLS-Zertifikate und SNI werden von der Anwendung eingerichtet. EdgeReverseProxy verwendet
die Kestrel-SNI-Capability bereits direkt.

Abnahme:

- Keine `ConfigureKestrelSni*`- oder `CertificateManager`-API verbleibt.
- `TlsProtocolFiltering` bleibt als Request-Level-Filter bestehen.
- RequestFilters referenziert weder Kestrel.Sni noch ein Certificates-Paket.

### RF-03: Hostaufbau, Directory-Layout und allgemeine Host-Helfer entfernen

Aktion: löschen, kein Paket in RequestFilters hinzufügen.

- [x] `Hosting/WebApplicationBuilderFactory.cs` entfernen.
- [x] `Hosting/WebApplicationBuilderDirectoryLayoutExtensions.cs` entfernen.
- [x] `Utilities/Storage/AppDirectoryLayout/AppDirectoryLayout.cs` entfernen.
- [x] `Utilities/IO/Directory/EnsureWriteableDirectoryExists.cs` entfernen, sobald keine Referenz mehr besteht.
- [x] `Utilities/IO/Directory/IsWritableDirectory.cs` entfernen, sobald keine Referenz mehr besteht.
- [x] `Utilities/Process/ProcessPath/TryGetExecutableDirectory.cs` entfernen.
- [x] `Utilities/Process/ProcessPath/TryGetExecutableNameWithoutExtension.cs` entfernen.
- [x] `Utilities/Process/ProcessPath/TryGetPrimaryFileLocation.cs` entfernen.

Capability außerhalb dieses Projekts:

- `Eigenverft.WebLib.Hosting.DirectoryLayout`
- darunter `Eigenverft.NetLib.Hosting.DirectoryLayout`

Begründung: Der Aufbau einer Anwendung und ihrer Verzeichnisse ist kein Filter-Scope. Der SQLite-Event-Speicher verwendet
seinen konfigurierten Pfad und BCL-Dateioperationen; er benötigt kein Directory-Layout-Paket.

Abnahme:

- Es verbleibt kein `WebApplicationBuilderFactory` oder `AppDirectoryLayout` in RequestFilters.
- Der SQLite-Event-Speicher baut und arbeitet weiterhin ohne DirectoryLayout-Abhängigkeit.

### RF-04: Anwendungskonfiguration und allgemeine Settings-Helfer entfernen

Aktion: löschen, keine der genannten Consumer-Capabilities in RequestFilters hinzufügen.

- [x] `GenericExtensions/WebApplicationBuilderExtensions/WebApplicationBuilderExtensions.cs` mit
  `AddDefaultConfigurationSources(...)` entfernen.
- [x] `Utilities/Settings/EncodedSettings/AddEncodedSettingsLayer.cs` entfernen.
- [x] `Utilities/Settings/Encoding/JsonEncodedSettings.cs` entfernen.
- [x] `Utilities/Settings/WritebackJsonStore/IServiceCollection.cs` entfernen.
- [x] `Utilities/Settings/WritebackJsonStore/WritebackJsonStore.cs` entfernen.
- [x] `Utilities/Diagnostic/LogConfigurationPrecedenceAndCollisions/LogConfigurationPrecedenceAndCollisions.cs`
  entfernen.

Capabilities außerhalb dieses Projekts:

- Default Sources: `Eigenverft.NetLib.Configuration.Sources`
- Encoded/umschaltbare Settings: `Eigenverft.NetLib.Configuration.Values`,
  `Eigenverft.NetLib.Configuration.SwitchableJson` und je nach Consumer `Sets`/`Transformations`
- Writeback: `Eigenverft.NetLib.Configuration.WritebackJson`
- Diagnostik: `Eigenverft.NetLib.Configuration.Diagnostics`

Begründung: Das ist Consumer-Migrationsinformation und ausdrücklich kein 1:1-Abhängigkeitsset für RequestFilters.

Abnahme:

- RequestFilters stellt keine allgemeine Configuration-Source-, Encoding-, Writeback- oder Konfigurationsdiagnostik-API
  mehr bereit.
- Keines der oben genannten Pakete wurde in RequestFilters referenziert.

### RF-05: Startup-Logging, Serilog-Brücken und allgemeine Service-Helfer entfernen

Aktion: löschen, keine Bootstrap- oder Serilog-Abhängigkeit in RequestFilters behalten.

- [x] `Utilities/Logging/BootstrapLogger/BootstrapLogger.cs` entfernen.
- [x] `GenericExtensions/LoggingExtensions/ToDeferred.cs` entfernen.
- [x] `GenericExtensions/LoggingExtensions/ToMicrosoftILogger.cs` entfernen.
- [x] `GenericExtensions/IServiceCollectionExtensions/AddAllowedHosts.cs` entfernen.
- [x] `GenericExtensions/IServiceCollectionExtensions/AddPermanentHttpsRedirection.cs` entfernen.

Capabilities außerhalb dieses Projekts:

- Startup-Logging: `Eigenverft.NetLib.Logging.Bootstrap`
- Host-Allowlist: ASP.NET Core Host Filtering
- HTTPS-/Host-Kanonisierung: native HTTPS-Redirection oder `Eigenverft.WebLib.CanonicalHostRedirect`
- Serilog: ausschließlich Anwendungskomposition

Abnahme:

- RequestFilters konfiguriert kein Startup-Logging und kennt keine Serilog-Typen mehr.
- Filter erhalten ihren Deferred Logger ausschließlich über DI.
- Keine Bootstrap-, CanonicalHostRedirect- oder Serilog-Capability wurde als Ersatzreferenz hinzugefügt.

### RF-06: Self-Warmup entfernen

- [x] Gesamten Ordner `Hosting/WarmUpRequests/` löschen, kein Paket in RequestFilters hinzufügen.

Capability außerhalb dieses Projekts:

- `Eigenverft.NetLib.Hosting.SelfHttpWarmup`
- Consumer-API `AddSelfHttpWarmup(...)`

Begründung: Selbstaufrufe einer Anwendung sind Hosting-Verhalten. Der historische Aufruf in App.ReverseProxy ist nur
auskommentiert und begründet keine RequestFilters-Abhängigkeit.

Abnahme:

- Keine Warmup-Option, kein Warmup-Hosted-Service und keine Warmup-Registrierung verbleibt.
- RequestFilters referenziert `SelfHttpWarmup` nicht.

### RF-07: Nicht filternde Middleware entfernen

Aktion: Verzeichnisse vollständig löschen. Die Capability-Pakete gehören ausschließlich in Anwendungen, nicht in
RequestFilters.

- [x] `Middleware/CanonicalHostRedirect/` entfernen; Consumer-Capability:
  `Eigenverft.WebLib.CanonicalHostRedirect`.
- [x] `Middleware/HealthProbeFaviconAware/` entfernen; Consumer-Capability:
  `Eigenverft.WebLib.HealthProbes`.
- [x] `Middleware/RequestLogging/` entfernen; Consumer-Capability:
  `Eigenverft.WebLib.RequestTrafficLogging`.
- [x] `Middleware/RequestDelayThrottling/` entfernen; nächstliegende Consumer-Capability:
  `Eigenverft.WebLib.RequestTrafficShaping`.
- [x] `Middleware/RequestRateSmoothing/` entfernen; nächstliegende Consumer-Capability:
  `Eigenverft.WebLib.RequestTrafficShaping`.
- [x] README- und NuGet-README-Tabellen sowie Pipeline-Beispiele um diese APIs bereinigen.

Wichtig: `RequestTrafficShaping` ist kein verhaltensgleiches Rename für die alten Delay-/Smoothing-Algorithmen. Es wird
nur als aktuelle Consumer-Capability genannt; RequestFilters darf dafür weder Adapter noch Kompatibilitätsfassade
behalten.

Abnahme:

- Unter `Middleware/` liegen nur noch Filter, Filterauswertung und die dafür nötige Client-Network-Integration.
- Keine der fünf Consumer-Capabilities wurde als RequestFilters-Abhängigkeit hinzugefügt.

### RF-08: Target Frameworks auf den Capability-Satz ausrichten

Aktion: vor dem Hinzufügen der fünf Pakete die Framework-Grenze bereinigen.

- [x] In `Eigenverft.Routed.RequestFilters.csproj` `net6.0` und `net7.0` entfernen.
- [x] Als Ziel `net8.0;net10.0` verwenden, weil alle fünf benötigten Capability-Pakete diese Frameworks unterstützen.
- [x] Die Framework-Reduktion als Breaking Change dokumentieren.

Begründung: Conditional Copies der alten Generics für net6/net7 würden das Ziel „Generics vollständig durch Pakete
ersetzen“ verhindern.

Abnahme:

- Beide Ziel-Frameworks bauen.
- Es existieren keine bedingten Legacy-Implementierungen für net6/net7.

### RF-09: Die fünf benötigten Capability-Pakete referenzieren

Aktion: ausschließlich diese direkten PackageReferences in `Eigenverft.Routed.RequestFilters.csproj` ergänzen:

- [x] `Eigenverft.WebLib.Middleware.Primitives`
- [x] `Eigenverft.WebLib.ClientNetwork`
- [x] `Eigenverft.NetLib.Networking`
- [x] `Eigenverft.NetLib.Configuration.Binding`
- [x] `Eigenverft.NetLib.Logging.Deferred`

Für die Implementierung sind die veröffentlichten, zueinander passenden WebLib-/NetLib-Releaseversionen zu verwenden.
Keine Aggregate-Pakete und keine lokalen `ProjectReference`-Abkürzungen einführen.

Abnahme:

- `dotnet list ... package --include-transitive` zeigt genau den erwarteten schmalen Eigenverft-Graphen.
- Keine Consumer-only-Capability erscheint als direkte RequestFilters-Abhängigkeit.

### RF-10: Lokale Middleware-Primitives durch das Paket ersetzen

Capability: `Eigenverft.WebLib.Middleware.Primitives`.

- [x] Alle Standard-Registrierungen der verbleibenden Filter auf das Paket-`UseMiddlewareOnce<TMiddleware>()` umstellen.
  Overloads mit expliziter Use-site-Konfiguration bleiben absichtlich eigenständige `UseMiddleware<TMiddleware>(...)`-
  Instanzen, weil jede ihren isolierten Options-Monitor besitzt.
- [x] Alle verbleibenden DI-Prüfungen auf das Paket-`EnsureServicesRegistered(...)` umstellen.
- [x] Alle nach den Löschpaketen verbleibenden Filter-Short-Circuit-Antworten auf das
  Paket-`WriteHtmlStatusResponseAsync(...)` umstellen.
- [x] Use-site-Konfiguration aller verbleibenden Filter über das Paket-`CreateUseSiteOptionsMonitor(...)` erstellen.
- [x] Für `FilteringEvaluationGateHttpContextMarkers` ein filtereigenes, typisiertes Feature behalten und es über
  `GetFeature`/`SetFeature` aus dem Paket speichern, statt allgemeine String-Keys in `HttpContext.Items` zu verwenden.
- [x] Danach folgende lokale Implementierungen entfernen:
  - `GenericExtensions/IApplicationBuilderExtensions/UseMiddlewareOnce.cs`
  - `GenericExtensions/IServiceProviderExtensions/EnsureServicesRegistered.cs`
  - `GenericExtensions/HttpResponseExtensions/WriteDefaultStatusCodeAnswer.cs`
  - `GenericExtensions/HttpContextExtensions/HttpContextPropertyExtensions.cs`
  - `Options/ConfiguredOptionsMonitor.cs`
  - `Utilities/HttpStatusCodeDescriptions/GetStatusCodeDescription.cs`

Abnahme:

- Keine lokale Kopie der genannten Infrastrukturtypen verbleibt.
- Standardregistrierungen jedes Filters wirken höchstens einmal; explizit separat konfigurierte Use-site-Instanzen
  bleiben möglich. Alle Registrierungen prüfen ihre benötigten DI-Services.
- Blockantworten und der öffentliche Filter-Evaluationsmarker verhalten sich wie zuvor.

### RF-11: Client-Adresse und CIDR über ClientNetwork/Networking beziehen

Capabilities:

- `Eigenverft.WebLib.ClientNetwork`
- `Eigenverft.NetLib.Networking`

- [x] In allen verbleibenden Filter-Pipelines, die Client-Adressdaten benötigen, `UseClientNetworkFeature()` statt
  `UseMiddlewareOnce<RemoteIpAddressContextMiddleware>()` verwenden.
- [x] In den verbleibenden Middleware-Implementierungen die Client-Adresse über eine filterinterne Abstraktion aus
  `IClientNetworkFeature.RemoteIpAddress` lesen.
- [x] Nur die vom ClientNetwork-Feature festgehaltene Peer-Adresse verwenden; die Migration wertet keine
  Forwarded-IP-Kette als zusätzliche Filterpolicy aus.
- [x] Für persistierte/geloggte Address-Strings `Normalize()`/`ToCanonicalString()` aus NetLib.Networking verwenden.
- [x] `DevelopmentUnlocker` für Override-Adressen auf `IPAddress.TryParse(...)` plus NetLib-Normalisierung umstellen.
- [x] Den eigenen CIDR-Parser und die Range-Mathematik in `Middleware/CidrFiltering/CidrFiltering.cs` durch
  `CidrNetwork` beziehungsweise `IPAddress.Matches(...)` ersetzen; Filterpolicy und Filterevents bleiben lokal.
- [x] Danach entfernen:
  - `Middleware/RemoteIpAddressContext/HttpContextExtension.cs`
  - `Middleware/RemoteIpAddressContext/RemoteIpAddressContext.cs`
  - `GenericExtensions/IPAddressExtensions/GetIpInfo.cs`

Abnahme:

- Es existiert kein eigenes Remote-IP-Context-Middleware und kein eigener IP-Normalisierer mehr.
- IPv4, IPv4-mapped IPv6, natives IPv6, ungültige CIDRs, `*`, Whitelist und Blacklist sind charakterisiert.
- Forwarded-IP-Information verändert ohne explizite Filterpolicy keine Entscheidung.

### RF-12: Options-Collection-Binding durch Configuration.Binding ersetzen

Capability: `Eigenverft.NetLib.Configuration.Binding`.

- [x] In allen verbleibenden Options-Klassen
  `OptionsConfigOverridesDefaultsList<T>` durch normale `List<T>`/Collection-Eigenschaften ersetzen.
- [x] In `SourceAndMatchKindWeightedFilteringEvaluatorOptions` den Wrapper durch ein normales Dictionary ersetzen.
- [x] Die jeweiligen Options-Registrierungen auf `BindReplacingCollectionDefaults(...)` umstellen; der Overload mit
  expliziter `IConfiguration` behält dabei diese Quelle und deren Change-Token bei.
- [x] Das Verhalten leerer Konfigurationscollections mit einem Charakterisierungstest festlegen und den passenden
  `EmptyCollectionBehavior` an jedem Bind-Aufruf ausdrücklich angeben.
- [x] Danach entfernen:
  - `Options/OptionsConfigOverridesDefaultsList.cs`
  - `Options/OptionsConfigOverridesDefaultsDictionary.cs`

Betroffene verbleibende Options-Bereiche:

- AcceptLanguage, BrowserBootstrap, CIDR, HostName, HttpMethod, HttpProtocol
- RemoteIpAddress, RequestSignature, RequestUrl, TLS, UriSegment, UserAgent
- Source-and-Match-Kind-Weighted Evaluator

Abnahme:

- Code-Defaults, konfigurierte Ersatzlisten, leere Listen und Reloads sind getestet.
- Keine Wrapper-Collection verbleibt im öffentlichen Modell.

### RF-13: Lokalen Deferred Logger durch Logging.Deferred ersetzen

Capability: `Eigenverft.NetLib.Logging.Deferred`.

- [x] Namespaces und DI-Registrierungen aller verbleibenden Filter, Evaluatoren und Event-Stores auf das Paket umstellen.
- [x] Sicherstellen, dass RequestFilters nur Microsoft Logging/Deferred Logging konsumiert und keine Logging-Pipeline
  konfiguriert.
- [x] Danach den gesamten Ordner `Services/DeferredLogger/` entfernen.

Abnahme:

- Keine lokale `IDeferredLogger`-/`DeferredLogger`-Implementierung verbleibt.
- Null-, InMemory- und SQLite-Event-Storage bauen mit dem Paketlogger.
- Keine Serilog-Abhängigkeit ist erforderlich.

### RF-14: Verbleibende filtereigene Pattern-Logik intern machen

Aktion: behalten, aber dem Filter-Scope eindeutig zuordnen.

- [x] `MatchesAnyPattern` und `IsRegExMatch` aus `GenericExtensions/StringExtensions/` in eine interne
  filter-spezifische Abstraktion verschieben, beispielsweise `Middleware/Abstractions/FilterPatternMatcher.cs`.
- [x] `FilterClassifier` und `BrowserBootstrapFiltering` auf diese interne Abstraktion umstellen.
- [x] Keine neue allgemeine String-Extension als öffentliche API anbieten.
- [x] Danach den leeren Ordner `GenericExtensions/` vollständig entfernen.

Abnahme:

- Wildcard-/Regex-Verhalten der Filter ist charakterisiert.
- Im Projekt existiert kein Namespace und kein Quellordner `GenericExtensions` mehr.

### RF-15: Paket- und Dependency-Cleanup

- [x] `CommunityToolkit.Diagnostics` entfernen; die Verwendung entfällt mit Kestrel-/Encoded-Settings-Code.
- [x] `Serilog` entfernen.
- [x] `Serilog.Extensions.Logging` entfernen.
- [x] `System.Security.Cryptography.ProtectedData` entfernen; BrowserBootstrap verwendet weiterhin ASP.NET Core
  `IDataProtection`, nicht dieses Paket.
- [x] `Microsoft.Data.Sqlite` und `SQLitePCLRaw.lib.e_sqlite3` behalten.
- [x] Prüfen, dass keine Aggregate `Eigenverft.WebLib.Infrastructure` oder `Eigenverft.NetLib.Infrastructure`
  referenziert werden.
- [x] Unbenutzte Usings, leere Legacy-Quellverzeichnisse und veraltete Migrationskommentare entfernen.

Abnahme:

- Der direkte Dependency-Graph enthält nur tatsächlich verwendete Pakete.
- `dotnet build` meldet keine durch die Migration entstandenen Warnungen oder Fehler.

### RF-16: Öffentliche Dokumentation und Release-Grenze aktualisieren

- [x] `README.md` ausschließlich auf die verbleibenden Filter und deren aktuelle Registrierung ausrichten.
- [x] `README.NUGET.md` entsprechend aktualisieren.
- [x] `AddPackageFiles/ReleaseNotes.txt` um alle entfernten öffentlichen API-Gruppen, die Framework-Reduktion und die fünf neuen
  Capability-Abhängigkeiten ergänzen.
- [x] Keine Anwendungs-Migrationsanleitung so formulieren, als würden Consumer-Capabilities transitiv über
  RequestFilters bereitgestellt.
- [x] Paketinhalt mit `dotnet pack` kontrollieren.

Abnahme:

- README, NuGet-README, Release Notes und öffentliche API widersprechen dem Quellcode nicht.
- Die Package-Beschreibung bezeichnet RequestFilters ausschließlich als Request-Filtering-Library.

### RF-17: Abschlussprüfung

- [x] Release-Build für `net8.0` und `net10.0` ausführen.
- [x] Charakterisierungstests für jeden verbleibenden Filter ausführen (Pipeline-Aktivierung für alle Filter sowie
  vertiefte Pattern-/CIDR-Tests).
- [x] Filter-Event-Erzeugung, EvaluationGate, DevelopmentUnlocker sowie Null/InMemory/SQLite-Storage testen.
- [x] Paketgraph mit `dotnet list package --include-transitive` prüfen.
- [x] Paket mit `dotnet pack` erzeugen und Inhalt sowie Framework-/Dependency-Metadaten kontrollieren.
- [x] Mit `rg` sicherstellen, dass entfernte Namespaces und Typen im Produktcode und in den READMEs nicht mehr vorkommen.
- [x] Prüfen, dass ausschließlich Dateien dieses Repositories geändert wurden; WebLib, NetLib, EdgeReverseProxy und
  App.ReverseProxy sind im abschließenden `git status --short` unverändert.

## Definition of Done

- [x] RequestFilters enthält nur Filter, Filterpolicy, Filterentscheidungen, Filterevents, Evaluation/Enforcement und
  filtereigenen Storage.
- [x] Alle allgemeinen Generics sind entweder durch eines der fünf benötigten Pakete ersetzt oder entfernt.
- [x] `GenericExtensions/`, allgemeines `Hosting/`, allgemeines `Infrastructure/` und allgemeine `Utilities/` sind aus dem
  Produktcode verschwunden.
- [x] Die nicht filternden Middleware-Verzeichnisse sind entfernt.
- [x] Genau fünf schmale Eigenverft-Capability-Pakete werden direkt verwendet; kein Aggregate-Paket und keine
  Consumer-only-Capability ist referenziert.
- [x] SQLite-Storage bleibt funktionsfähig und zieht kein DirectoryLayout ein.
- [x] EdgeReverseProxy und alle anderen Repositories wurden nicht verändert.
- [x] Build, Tests, Pack und Dependency-Graph sind grün beziehungsweise geprüft.
