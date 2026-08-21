# Android Client

The Android client captures and reviews purchase invoices. It never contains SQL or Gemini credentials and never writes directly to Genius.

## Phase 1 Baseline

- Permanent `applicationId` and namespace: `com.pharmaauto.android`.
- `minSdk 28`, `compileSdk 37`, `targetSdk 37`.
- Kotlin `2.4.10` with AGP built-in Kotlin and the Compose compiler plugin.
- Jetpack Compose with Material 3 BOM `2026.08.00`.
- CameraX `1.6.1`, Hilt `2.60.1`, Room `2.8.4` and WorkManager `2.11.2`.
- Retrofit, OkHttp and Kotlinx Serialization for the Local Connector API.
- Firebase App Distribution plugin `5.3.0`; project `pharma-auto-eg-smartsolustions`, Android package registration, and the `internal-testers` group are initialized. No App ID, token, or service credential is committed.
- EGP decimal-string contracts; selling price is tax-inclusive per `BOX` and applies to new stock only.

## Implemented Read-Only Slice

- One-time deep-link pairing with an Android Keystore P-256 key and pinned Connector TLS certificate.
- In-app CameraX capture plus JPEG/PNG/PDF magic, size, dimension, active-content and quality checks.
- Connector-scoped Room drafts and resumable, hash-bound WorkManager uploads.
- Local Vendor/Item candidate lists and full-catalog search; no identity is auto-selected and hard mismatches are disabled.
- Independent `BigDecimal` calculation for the two sequential percentage discounts.
- Discount 1 changes the purchase-unit-price path.
- Discount 2 applies to the remaining line subtotal.
- A hard policy rejects global repricing or any change to old-stock prices.
- English and Arabic resources with RTL support.
- Unit tests for the calculation and old-stock guard.
- Material 3 invoice review screen with previous/next Item navigation and preserved OCR source values.
- Editable purchase price, both discounts and selling price for every Item.
- Add, edit, split and remove expiry rows; every expiry owns its quantity and date, and mismatched quantities block review completion.
- Invoice totals and a confirmation surface designed for non-technical operators. Confirmation creates an immutable Connector revision and checks that `commitAvailable` and `geniusWritePerformed` remain false.

The retained preview composable is still useful for design previews, but the application route now uses the Connector-backed review package and persists confirmed read-only state. No Genius Commit exists in Phase 1.

## Local Setup

Requirements:

- JDK 17.
- Android SDK Platform `37.0`.
- Android Build Tools `36.0.0`.
- Android CLI and Kotlin CLI `2.4.10` are recommended for environment and pure-domain checks.

Install the required Android SDK packages:

```powershell
android --no-metrics --sdk=$env:ANDROID_SDK_ROOT sdk install platforms/android-37.0 build-tools/36.0.0 platform-tools
```

Build and lint:

```powershell
$env:JAVA_HOME = "C:\path\to\jdk-17"
$env:ANDROID_SDK_ROOT = "C:\path\to\android-sdk"
./gradlew.bat lintDebug assembleDebug
```

Compile the ERP-neutral commercial domain independently with Kotlin CLI:

```powershell
kotlinc app/src/main/java/com/pharmaauto/android/domain/CommercialValues.kt `
  -Werror -jvm-target 17 -d commercial-domain.jar
```

## Firebase App Distribution

Firebase project `pharma-auto-eg-smartsolustions` contains the registered Android app for `com.pharmaauto.android`. Keep its Firebase App ID outside the repository and upload explicitly:

```powershell
firebase login:list
firebase apps:list ANDROID --project pharma-auto-eg-smartsolustions
./gradlew.bat appDistributionUploadDebug `
  -PFIREBASE_APP_ID="1:PROJECT_NUMBER:android:APP_HASH" `
  -PFIREBASE_GROUPS="internal-testers"
```

The `internal-testers` group exists but contains no tester addresses yet. The debug APK intentionally keeps `com.pharmaauto.android`; it does not add an application-ID suffix that would require a second Firebase app registration. Upload remains an explicit release action and is not part of build or CI. No upload or billable cloud-device run was performed during initialization.

No Firebase upload or paid cloud-device run is part of Phase 1 verification.

## Boundaries

- Android talks only to the paired Local Connector.
- The Connector-backed flow creates immutable Invoice Revisions and correction audit evidence, but confirmation never exposes a Commit.
- OCR source text remains visible and unchanged; confirmed values are separate.
- Mixed Arabic/English labels use content-derived direction and Unicode isolation in UI only.
- A Local Connector response must expose authorization and impact before any consequential confirmation.
