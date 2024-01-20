# DonAlertsInt

Separate server for Donation Alerts Centrifugo listener

## WHY
I failed to integrate DonationAlertsApiClient into Unity, since Unity fails to connect to donation alerts. Believing stackoverflow - that's because Unity uses some outdated lib for websockets and such connections are declined by donation alerts API.

So this console server actually interacts with Donation Alerts and serves HTTP actions for the game.

# Building

Nothing too fancy.

I built this project using `.NET SDK 8 (8.0.1)`, although it's targeted at `net7.0`

I built this project in `Rider 2023.1.2`

Just open and run.

Alternatively

`dotnet build -c Release` in the root folder.