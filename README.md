# StudioLog
Timecode Logging

## Google Drive Export Setup

StudioLog can upload PDF/CSV/PNG session logs straight to a Google Drive Shared Drive folder (`File > Export > Google Drive`). This repo is public, so no Google credentials are checked in — each installation needs its own local credentials file, created once per machine by following the steps below.

### 1. Create a Google Cloud project

1. Go to [console.cloud.google.com](https://console.cloud.google.com) and sign in with the Google Workspace account you want StudioLog to use (e.g. your `@yourcompany.com` account).
2. Click the project dropdown at the top → **New Project**.
3. Name it (e.g. "StudioLog"), leave the organization as your Workspace org, and click **Create**.
4. Wait for the notification that the project was created, then select it from the project dropdown.

### 2. Enable the Google Drive API

1. With your new project selected, go to **APIs & Services > Library** (or [console.cloud.google.com/apis/library](https://console.cloud.google.com/apis/library)).
2. Search for **Google Drive API** and open it.
3. Click **Enable**.

### 3. Configure the OAuth consent screen

1. Go to **APIs & Services > OAuth consent screen** (or **Google Auth Platform** in the left sidebar).
2. Click **Get started**.
3. **App name**: `StudioLog`. **User support email**: your email.
4. **Audience**: choose **Internal**. This restricts sign-in to accounts in your Google Workspace organization, skips Google's app verification process, and avoids a 7-day refresh-token expiry that applies to unverified public apps.
5. **Contact information**: your email.
6. Agree to the Google API Services User Data Policy and click **Create**.

### 4. Create an OAuth Client ID

1. Go to **APIs & Services > Credentials** (or **Clients** under Google Auth Platform).
2. Click **+ Create client** (or **Create credentials > OAuth client ID**).
3. **Application type**: **Desktop app**.
4. **Name**: `StudioLog Desktop`.
5. Click **Create**. A dialog shows your **Client ID** and offers a **Download JSON** button — download it, or copy the Client ID and Client Secret shown.

### 5. Save the credentials for StudioLog

Create the file `%AppData%\StudioLog\google-credentials.json` (e.g. `C:\Users\<you>\AppData\Roaming\StudioLog\google-credentials.json`) with this content, filled in from step 4:

```json
{
  "ClientId": "your-client-id.apps.googleusercontent.com",
  "ClientSecret": "your-client-secret"
}
```

If you downloaded the JSON file from Google instead, its keys are named `client_id`/`client_secret`/`installed` — just copy those two values into the format above; StudioLog expects exactly `ClientId` and `ClientSecret`.

### 6. First use

The first time you use `File > Export > Google Drive`, your browser will open to a Google sign-in/consent screen. Approve access with the same account from step 1 — StudioLog caches the resulting token, so this only happens once (or again after `Settings > DISCONNECT GOOGLE DRIVE`).

**Note:** the folder picker starts at a Shared Drive named **Production**, inside a folder named **Artists**. If your Drive doesn't have that structure, it falls back to showing all Shared Drives so you can browse to wherever you want.
