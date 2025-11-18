MISE Compliance KPI eSTS TSG
[!INCLUDE safefly-banner.md]

[!INCLUDE clickops-banner.md]

Announcements and Updates
⚠️Exception Process: The MISE KPI Exception process is documented here .
⚠️Office Hours are held Mondays and Wednesdays from 9-10am PST: https://aka.ms/mise/officehours 

⚠️IMPORTANT UPDATE - Updated: 2025-09-10
KPI Processing Logic Update: Auto-Closure Based on Inactivity changed to 30 days
KPI items now automatically close after 30 days.
Previously, items would auto-close after ~3 days without token acquisition. KPI items will now remain open for 30 days before auto closing. For applications that have been deleted or disabled, the KPI flow will be updated to automatically remove these items.
An alternative for now is to delete your app and request a KPI extension, MISE Compliance Exception Policy 

⚠️NOTE - Updated: 2025-01-06
MISE Compliance Announcement: dSTS KPI TSG
Please see MISE Compliance S360 dSTS KPI Troubleshooting Guide.md).

⚠️NOTE - Updated: 2024-12-19
MISE Compliance Announcement: New Minimum Version Required - 1.31.0
Please see this announcement for more details.

⚠️MISE & EasyAuth - Updated: 2024-12-02
If your application is leveraging EasyAuth, it will currently be reported as not compliant.
Please see this announcement for more details.

Overview
How does the MISE Compliance KPI Work?
Any application (Entra app registration) that has had an access token issued for it is in scope for the KPI. (see below for tenants in scope)
If access tokens were successfully issued for your application, then there must be calls from your application to Entra, identifying itself as your application, and retrieving keys to validate the tokens.
For this example, we have an application registered in Entra with the Application ID “a482ee0f-2335-44c4-8750-0997cfad3c2b” and the ApplicationDisplayName “MISE QnA Api”.

App Sample

There are three primary parts to consider in the KPI evaluation flow.
Please note, there are simplified query examples.

A client application requests an access token for your services/API. We'll call this step “Token Acquisition”.
In this example, we can see that 3 different applications are requesting access tokens for our service.
The ‘ResourceId’ is the resource application that the token is intended for (the audience).
The 'ApplicationId' is the client application that requested the token.
token request example

The client application should then make a call to your services/API, using that access token in the authorization header.
Your service/API receives the call and validates the access token with MISE. We'll call this "Token Validation". The running MISE instance will report the telemetry.
Let’s look at the MISE Telemetry for our application.
token vaidation sample

Here we see our application was on version 1.22.2 on the 12th and 13th. One the 14th, we reported two versions of MISE. This was probably during a deployment/update. On the 15th only 1.28.2 was reported.
However, it will take two more days before the KPI item is marked as complaint and resolved. Why?

1.28.2 (in this example) is the minimum required version.
The KPI requires 3 consecutive days of only compliant MISE telemetry
In summary, if there are clients successfully getting tokens for your application, then there MUST be calls from your application calling Entra identifying itself as your application and retrieving keys to validate the tokens

More precisely, the MISE Compliance KPI is enforcing that for any application on a Microsoft Infrastructure tenant (see below for definition) which is a resource for an access token successfully issued by Entra ID (in Public PROD and USGov/Fairfax/Arlington in the past 3 days), those tokens must be validated by the Microsoft Standard MISE library .

Microsoft Infrastructure Tenants in Scope

This means that as long as there are requests to acquire access tokens for an Application ID/Resource Id owned by your team, and that Application ID does not have a MISE counterpart validating the tokens, your team will be flagged to migrate the token validation to MISE.

Links to MISE documentation
💡 How to be compliant with MISE, which describes the experience from the service health dashboard.
🔏 Getting Started with MISE , provides technical details on how to adopt MISE and resolve the S360 KPI.

I've been using MISE for a long time, why do I suddenly see an S360 item?
You must be on the latest version of MISE Microsoft.Identity.ServiceEssentials.AspNetCore package in IDDP feed in Azure Artifacts
While the KPI only requires a specific version or above, we always recommend to upgrade to latest if you're taking any action.

Do the minimum versions of MISE need to be in PROD? Or in any slice?
For applications in the Microsoft services tenant, the KPI is only taking PROD environments into consideration (not test slices and PPE). For apps in the infrastructure tenant (see below for complete list), the KPI takes into account all traffic.

What happens when telemetry shows that the work for the KPI is done?
The MISE Compliance KPI will no longer appear in your S360 dashboard as all action items were accounted for.

What is the latest version of MISE?
It is strongly recommended to always take the latest version of MISE.

Latest

While the current KPI only requires 1.31.0 or above, we always recommend upgrading to latest if you're taking action.

Troubleshooting Steps
You have completed the work to migrate to MISE, but the KPI is still showing up as open.

MISE Host Configuration - ClientId
Once of the most common issues is a misconfiguration in the MISE host. Usually do to the wrong application ID is being used.

Here is example of an appsettings.json. In this example, the clientId is the ID of your application. There is some confusion about the term 'client' as in the queries 'client' is used to refer to the application requesting the token. However, in this context, it is your application. This is the ID your application uses to identify itself to Entra and this is the ID that MISE will use to send the telemetry.

The applicationId in the MISE telemetry must match the resourceId that the token was requested for.
This is the ID from our sample application in the How does the MISE Compliance KPI Work walkthrough.
As pointed out there, if this is not set correctly the IDs will not match and the KPI will remain active.


"AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "common", // for AAD + MSA users
    "ClientId": "a482ee0f-2335-44c4-8750-0997cfad3c2b",
    "Audience": "api://a482ee0f-2335-44c4-8750-0997cfad3c2b",
}
If you are using multiple Inbound Policies, be sure the client is set correctly in each.

In other case it's because there are flows in your apps, that are using SAL but not MISE.

Go to the aka.ms/mise/dashboard  PowerBI dashboard and enter your client ID/Application ID in the search box of the ApplicationId column to get details on the current telemetry. This report also has a tooltip that provides an explanation for the S360 status/compliance status:
help tooltip

NOTE: If for some reason you see a compliance status of "Unknown" for your app, this is due to our data still being in the process of refreshing. For an up-to-date compliance status, please check in the next time the report refreshes (refresh times are 8:00, 12:00, and 16:00 PST).

If further details are needed on client telemetry or MSAL usage, you can also look up your application ID in the aka.ms/mise/servicehealth  Power BI report. NOTE: This report will only allow you to view the services you are considered an owner of.
Check if your service is using Microsoft.WindowsAzure.Arm.Infra.ARMAuthenticationAAD library with a version lower than 1.0.582. If so, upgrade the library to the latest. Versions older than 1.0.582 are using only SAL (and not MISE) which can make the KPI to flip from green to red.
Run the MISE Telemetry yourself
Please see the page - Running the MISE Telemetry - Overview

Frequently Asked Questions
I'm having a hard time getting access to IDP telemetry to double check the MISE compliance calculation. Is there another way to see more details on the traffic I'm being flagged for?
Yes! You can now use the Service Health Dashboard  to view the call history for your applicaiton (for both eSTS and dSTS).

First find the app you wish to view the history for then right click on the ApplicationId and select Drill Through > App Call History.

Drill Through Menu SHD

When you click through you'll see two sets of charts which correspond to the 30 day history of the data returned by the queries in the Running the MISE Telemetry section.

App Call History Dashboard

As described elsewhere in the TSG, for every day (and in every cloudType) you see Token Acquisition traffic there MUST be Key Fetch History traffic.

How long does it take for the S360 KPI to closed or resolved?
The S360 KPI requires that the past 24 hours of traffic show compliant traffic.
Note that the data pipeline pulls the data once per day so depending on what time of day your fix goes in, you may have to wait less time or a bit more (e.g. if the data hadn't fully replicated by the time it was pulled).
Assuming all implementation work and deployments are complete. Please check the telemetry for your application. Running the MISE Telemetry

If you have implemented MISE and are seeing compliant traffic, the KPI should close within 24-48 hours. Note that the data pipeline pulls the data once per day so depending on what time of day your fix goes in, you may have to wait less time or a bit more (e.g. if the data hadn't fully replicated by the time it was pulled).
Running the MISE Telemetry to confirm there is no recent token activity.

Important: The KPI flow is being updated to automatically detect and remove KPI items for applications that have been deleted or disabled.
Once tokens are no longer being issued, the KPI will resolve in 30 days.

An alternative for now is to delete your app and request a KPI exception, send to us and ask for approval at MISE Compliance Exception Policy 

Why do I have an open S360 item in Fairfax cloud when my Public Cloud S360 item has been fixed? Especially if they use the same or similar code?
The Public cloud and Fairfax cloud represent two different instances of eSTS/Entra. There are some aliases but for the most part, Public is represented by https://login.microsoftonline.com and Fairfax is represented by https://login.microsoftonline.us.

Today both clouds publish both sets of keys (i.e. the public keys for Public and Fairfax) because of this, if you configure both of your services with https://login.microsoftonline.com or https://login.microsoftonline.us your service will work but your MISE instance will not be communicating with the correct eSTS/Entra instance.

This is something being addressed by a separate security effort so you will be forced to address this anyways, the best fix for now is, if you have traffic against some cloud that's acquiring a token you must have an instance of MISE configured with that authority. In most cases this will mean an application with the https://login.microsoftonline.com authority and another for https://login.microsoftonline.us authority.

What does it mean when there's an empty MISE version and my app is showing not compliant?
For example:

On 08/14/24, MISE: ["","1.22.4.0","1.25.0.0"] and/or SAL: ["4.6.0.0","4.4.0.0","4.7.0.0","4.3.0.0"] key discovery (server) telemetry with uncompliant version(s) was present (< 1.28.2 for MISE or < 4.2.5 for SAL).

While 1.22.4.0 and 1.25.0.0 are compliant, there's also an empty quotation version there which effectively means that an instance of your app is only using SAL (the internal AuthN module for MISE) without using MISE proper. Please follow https://aka.ms/mise/1p  to move completely onto MISE.

You can also use the eSTS queries above to see the subnet for which IP Subnets have RPs with the blank version to help you track down the non-compliant instances.

My app is listed with the state of Exception, what does that mean?
legacy exception example

All new exceptions for the MISE Compliance KPI are driven through S360 and that should be the source of truth. There was a legacy exception process which blocked the creation of S360 KPIs that is being phased out. Some of these apps will continue to have an exception but others will be cleared out as they were given exceptions in error. We are targeting legacy exceptions to be cleared out ahead of the start of [Wave 3].

My app is a WebApp/Website, we only request apps and do not validate any tokens, why are we getting this?
The KPI works by detecting all successful token requests in PROD and ensuring all the apps those tokens are for are using MISE. This likely means either you have tests hitting PROD requesting tokens for your app or someone else is creating tokens for your app. If there is token traffic, it must be validated by MISE so the S360 is valid. MISE Docs  have steps on how to run the telemetry and see what apps are calling your app; anything in the past 30d will make your KPI red if there's not corresponding validation.

The S360 details say there's no historical SAL/MISE data. What does that mean?
Your S360 status details may have something like the following block of text:

...were acquired for this app ID but no MISE/SAL key discovery (server) telemetry was present. There is also no historical MISE/SAL server telemetry data available for this app ID.

This implies that we have not ever seen telemetry indicating your application is leveraging MISE to validate requests. If you have not yet started onboarding to MISE please follow https://aka.ms/mise/1p . If you believe you have already onboarded onto MISE please keep reading, the next question may help you.

I have logs in my application showing I'm validating tokens with MISE but that doesn't show up in the query above, what's going on?
Some services will have fully upgraded to MISE and can see in their application logs MISE is validating tokens. You may see a log like the following:
MISE12019: AuthenticationTicketProvider HttpContextAuthenticationTicketProvider (1.25.0.0) successfully validated the request.
However, the dashboard and S360 still reflect no MISE usage. In most cases, this is caused by MISE being misconfigred to use a different applicationID when either the MiseHost or the InboundPolicy is created. In order for the telemetry to properly count for your application and allow it to go green, the ClientId must be set to the applicationId of the app in question. If you are still having trouble, please post your question in the https://aka.ms/mise/help  teams channel and be sure to include a link to your code where you setup your MiseHost as well as instructions on how to get permission to access your repo if necessary.

I've deleted/disabled the app this KPI is calling out, but I still see it as red
If you are still seeing a KPI for a deleted app after 30 days please re-confirm it's actually deleted and confirm there is no recent token activity using the telemetry queries.
In case your KPI is set to expire before the 30 days of no telemetry, you can file an extension request as shown in the screenshot below but note this requires your team CVP or Partner approval and you can only get it for one month at a time.
If issues persist, reach out on the teams support channel .

Extension Request Screenshot

I am unable to view details on the "Service Health Dashboard"
Only a Service Owner in ServiceTree  can view data in Service Health Dashboard .
If you are not seeing data, then reach out to the service owner for the ServiceTreeId (ST Id) and ask to be added to the service as either a Dev Owner or PM Owner or as an Admin.

Power BI reports are not accessible, and request permissions page is shown for non-FTEs
If you are not an FTE, then you would need to perform the below steps to access MISE Dashboard  and Service Health Dashboard 

Become a member of the DL MiseDashboardNonFTEUsers . This request is auto-approved.
Once this is approved and access provisioned in all systems (might take 12-24 hrs) MISE Dashboard  is accessible.
To access Service Health Dashboard  one needs to be a member of DLMiseDashboardNonFTEUsers  and also be the Service Owner. Refer to Unable to view details on the "Service Health Dashboard"
I have an S360 item, but my app doesn't show up in the dashboard
Some documentation is still erroneously pointing at https://aka.ms/adaltomsal  which does not have the full dataset being reported.
Please report these documentation bugs and we will work through fixing them.
You should be using the MISE Dashboard  and Service Health Dashboard  for the most up-to-date information on the MISE Compliance KPI.

My Application is marked as "Stale MISE traffic" and the S360 remains open or reoccurs
Stale MISE example dashboard

Stale MISE traffic indicates that in the most recent day(s) there has been token acquisition traffic for this application, however there hasn't been any MISE token validation traffic. The KPI is evaluated daily, so you must ensure that each day there is token acquisition there is also token validation using MISE.

If tokens are being acquired for this app ID but the app is not validating them the KPI will remain open or reoccur with a reason such as:

Stale MISE tooltip

On 08/26/24, 35 token(s) were acquired for this app ID but no MISE/SAL key discovery (server) telementry was present. MISE: ["1.26.0.0"] and/or SAL: ["4.7.0.0"] versions were last seen on 08/24/24

Even if the MISE version(s) seen in the past were compliant, the KPI will occur because the most recent telemetry is telling us this is an insecure app due to not all tokens are getting validated with that compliant version. You can use the tooltip present in aka.ms/mise/dashboard to get more information on when MISE version(s) for this application ID were last seen.

To resolve this, app owners must either:

Use the token acquisition query above to track down the callers acquiring tokens but not validating them.
Ensure all RPs are validating using MISE, and that it's not the case that some RPs are using some non-MISE solution while others are using MISE (which would result in reporting Stale).
Verify that the ClientId in the AzureAd configuration section matches the application ID that tokens are being acquired for. For applications with multiple inbound policies, ensure tokens are validated using the correct inbound policy with the expected ClientId.
If you have overridden the default metadata refresh time of 12 hours you would want to reset this to something which would have at least one instance of your RP fetching metadata with your MISE enabled RP no less than every 24h.

Why is this app showing up assigned to me?
We track ServiceTree attribution via app registration. The field which sets the ServiceTree link is not required to be set except in Microsoft Service so it may be blank (or set incorrectly). In the case it's blank heuristic fallbacks are used. If you think it's set incorrectly you can follow the steps here to correct it . The team backing that form manually updates twice a week and our KPI updates 3 times a week so expect about 1 week of latency after submitting the form.

The app flagged is a test app, why is this in scope?
Any application which has a token requested for it successfully in PROD is in scope. We have seen attacks which leverage insecure test apps so these need to remain in scope.

On the Service Health Dashboard App Audience Validation Compliance is red. Does this impact my MISE Compliance S360 KPI?
App Aud Validation

No. As called out in the link from that section of the dashboard:

⚠️ NOTE: This audience validation guidance is not currently being evaluated in any SFI Wave 1 or Wave 2 KPIs. We expect it will be enforced in Wave 3 so we recommend you to opportunistically fix it now but acknowledge you may wish to prioritize other Wave 1&2 asks over this work.

I'm unable to connect to the estsch1 Kusto cluster
This is covered in the Running the MISE Telemetry - Overview page.

Accessing the Azure Data Explorer (Kusto) Cluster
The ClientIp Subnet is not enough to find which RPs are running older versions of MISE (or no MISE). How can I get the exact IPs?
If you need the actual IP addresses of these calls, you'll need to work with the eSTS OCE  Please provide them with the following information:

For ESTS OCE: Use this to get access for ClientIPs - JIT Access to EUII and CC in ESTS PerRequestTable - Overview
Assuming you have a subnet of "20.44.0.000", make sure to remove the additional trailing zeros on the end of the subnet. So, the proper query to run to look up requests in this case is:

| where ClientIpSubnet == "20.44.0.0"
For a more concrete example, please visit MISE Compliance TSG Telemetry 
Once you have the IPs you can work with AzureNetworking or ReportItNow to understand who is impersonating your app with a non-compliant version of MISE (or if it was some test app you weren't able to previously ID).

Azure Networking team recommends following this process to link IPs to Subscriptions .

Why does the dashboard say I have "client" telemetry but no "server" telemetry
token(s) were acquired for this app ID but no MISE/SAL key discovery (server) telemetry was present. There is also no historical MISE/SAL server telemetry data available for this app ID.
Client telemetry (not used for the KPI at this time) was present with the following MISE version(s) within the last 3 days: ["1.28.2.0"].

More than likely this indicates that you have either updated your service correctly and should expect the KPI to close the next time our data aggregation job runs OR it means that you've adopted MISE and set the correct appId but it is pointing at the incorrect authority (perhaps PPE instead of PROD). You can confirm you're on the right track and should go green soon by running the eSTS queries yourself (documented above).

I work with a vendor/non-FTE who does not have access to this wiki or MISE docs
This security group is setup to give them access to this page as well as https://aka.ms/mise/1p  (as well as the general MISE dashboard, https://aka.ms/mise/dashboard  --they'll have to be an owner in ServiceTree to access the Service Health Dashboard as noted in another quesion)

Become a member of the DL MiseDashboardNonFTEUsers . This request is auto-approved.

My app must have tokens requested for it but I have a good justification for why this will not comprimise security if not validated by MISE
Please follow the steps in this TSG.

My development/test application is being flagged as stale, but it should not be subject to MISE validation according to the documentation
If your application is used for development or testing purposes, you should follow the eSTS guidance for implementing authentication in non-production environments. See the Test Slice Environment (TSE) documentation  for guidance and best practices on configuring the appropriate authentication endpoints for your development and test applications.

I've deleted all my Azure deployments. Why do I still have a KPI?
The KPI is not driven by deployed compute instances. It is driven by access token requests.
You can have all your virtual machines or app services deleted, but if the application registration is still present in eSTS/Entra, then tokens could still be requested.
You can find what client applications are requesting access tokens for your resource application by using the MISE Telemetry.
Running the MISE Telemetry - Overview

If this application is no longer in use, you should look at disabling or deleting the application registration.
Be sure to confirm this is no longer being used anywhere and will not cause any issues before taking any delete actions.

Important: The KPI flow is being updated to automatically detect and remove KPI items for applications that have been deleted or disabled.
Once tokens are no longer being issued, the KPI will resolve in 30 days.

An alternative for now is to delete your app and request a KPI exception, send to us and ask for approval at MISE Compliance Exception Policy 

Why do I see the MISE version x.y.z.a when the version is not deployed in PROD and is not supported for our service?
Use the https://aka.ms/mise/shd  report to get the IP address for the version x.y.z.a. In the MISE Details report tab, in table "MISE App Details" right click on an ApplicationId record and select the option Drill through --> IP Address Drillthrough. This will open the Drillthrough report having IP address details for all the versions used by the RP(ApplicationId). To navigate to the MISE Details report tab click on the back arrow button at the top of the Drillthrough report page.
NOTE: The Drillthrough report will show data for ApplicationIds having two or more MISE Versions and MISE Compliance Status as Yellow. Apps having MISE Version BLANK will not have any IP address and also apps not having client telemetry will not have IP address.

Microsoft Infrastructure Tenants in Scope
The vast majority of apps will fall into these primary tenants which are in scope for the MISE Compliance KPI:

Microsoft Service Tenant (f8cdef31-a31e-4b4a-93e4-5f571e91255a)
MSIT (72f988bf-86f1-41af-91ab-2d7cd011db47)
PME (975f013f-7f24-47e8-a7d3-abc4752bf346)
AME (33e01921-4d64-4f8c-a055-5bdaffd5e33d)
GME (124edf19-b350-4797-aefc-3206115ffdb3)
Torus (cdc5aeea-15c5-4db6-b079-fcadd2505dc2)
XPME Test Tenant (9d8a5284-143b-4bde-82fd-d82b6bccb5fc)
Partners Tenant (a5f51bc5-4d47-4954-a546-bafe55e8db16)
Additionally any apps which belong to any of the tenants in this query will also generate a KPI provided the app has proper attribution in ServiceTree (ensuring attribution is part of a separate SFI effort):


cluster('amemetrics.kusto.windows.net').database('TenantSecurityAndIsolation').SFI_EntraIDTenantsScope
| where ManagedBy == "IAM Protect"
| where TenantType != "Test"
| where TenantType != "Productivity-Test"
Contact Us
If you have additional questions that are not answered by this troubleshooting guide, please post your questions in our MISE KPI Problems and Questions Teams Channel 