# Android Client

The Android client captures and reviews purchase invoices. It never contains SQL or Gemini credentials and never writes directly to Genius.

## Initialized Baseline

- Permanent `applicationId` and namespace: `com.pharmaauto.android`.
- `minSdk 28`, `compileSdk 37`, `targetSdk 37`.
- Kotlin `2.4.10` with AGP built-in Kotlin and the Compose compiler plugin.
- Jetpack Compose with Material 3 BOM `2026.08.00`.
- Retrofit, OkHttp and Kotlinx Serialization for the Local Connector API.
- Firebase App Distribution plugin `5.3.0`; project `pharma-auto-eg-smartsolustions`, Android package registration, and the `internal-testers` group are initialized. No App ID, token, or service credential is committed.
- EGP decimal-string contracts; selling price is tax-inclusive per `BOX` and applies to new stock only.

## Implemented Initialization Slice

- Contract-aligned commercial values and Local Connector preview DTOs.
- Independent `BigDecimal` calculation for the two sequential percentage discounts.
- Discount 1 changes the purchase-unit-price path.
- Discount 2 applies to the remaining line subtotal.
- A hard policy rejects global repricing or any change to old-stock prices.
- English and Arabic resources with RTL support.
- Unit tests for the calculation and old-stock guard.
- Material 3 invoice review screen with previous/next Item navigation and preserved OCR source values.
- Editable purchase price, both discounts and selling price for every Item.
- Add, edit, split and remove expiry rows; every expiry owns its quantity and date, and mismatched quantities block review completion.
- Expandable invoice totals and finish-review surface designed for non-technical operators. Totals are explicitly unavailable while required values are invalid or incomplete.

The first API slice is preview-only. Its finish action validates in-memory synthetic state; it does not persist or send a revision and does not imply a Genius Commit.

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

## Boundaries

- Android talks only to the paired Local Connector.
- The future Connector-backed flow creates immutable Invoice Revisions and correction audit evidence; the current Android initialization screen does not persist them.
- OCR source text remains visible and unchanged; confirmed values are separate.
- Mixed Arabic/English labels use content-derived direction and Unicode isolation in UI only.
- A Local Connector response must expose authorization and impact before any consequential confirmation.
