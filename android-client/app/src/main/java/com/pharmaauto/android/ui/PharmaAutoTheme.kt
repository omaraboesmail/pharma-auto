package com.pharmaauto.android.ui

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Shapes
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp

private val ForestGreen = Color(0xFF075D35)
private val ForestGreenDark = Color(0xFF00391F)
private val ConfirmedGreen = Color(0xFFE8F5EB)
private val PaleGreen = Color(0xFFC6F0D5)
private val Ink = Color(0xFF171D19)
private val Canvas = Color(0xFFF9FBF7)
private val CoolSurface = Color(0xFFF0F3EF)
private val Amber = Color(0xFF8A5100)

private val LightColors = lightColorScheme(
    primary = ForestGreen,
    onPrimary = Color.White,
    primaryContainer = PaleGreen,
    onPrimaryContainer = ForestGreenDark,
    secondary = Color(0xFF4C6354),
    onSecondary = Color.White,
    secondaryContainer = ConfirmedGreen,
    onSecondaryContainer = Color(0xFF0A2A19),
    tertiary = Amber,
    onTertiary = Color.White,
    tertiaryContainer = Color(0xFFFFDDB5),
    onTertiaryContainer = Color(0xFF2C1600),
    background = Canvas,
    onBackground = Ink,
    surface = Color.White,
    onSurface = Ink,
    surfaceVariant = CoolSurface,
    onSurfaceVariant = Color(0xFF414943),
    outline = Color(0xFF707972),
    outlineVariant = Color(0xFFC0C9C1),
    error = Color(0xFFBA1A1A),
    onError = Color.White
)

private val DarkColors = darkColorScheme(
    primary = Color(0xFF88D5A4),
    onPrimary = Color(0xFF00391F),
    primaryContainer = Color(0xFF00522E),
    onPrimaryContainer = Color(0xFFA4F2BF),
    secondary = Color(0xFFB4CCBB),
    onSecondary = Color(0xFF203529),
    secondaryContainer = Color(0xFF354B3D),
    onSecondaryContainer = Color(0xFFD0E8D6),
    tertiary = Color(0xFFFFB95F),
    onTertiary = Color(0xFF482900),
    background = Color(0xFF101411),
    onBackground = Color(0xFFE0E4DE),
    surface = Color(0xFF101411),
    onSurface = Color(0xFFE0E4DE),
    surfaceVariant = Color(0xFF252A26),
    onSurfaceVariant = Color(0xFFC0C9C1),
    outline = Color(0xFF8A938B),
    outlineVariant = Color(0xFF414943),
    error = Color(0xFFFFB4AB),
    onError = Color(0xFF690005)
)

private val PharmaAutoShapes = Shapes(
    small = RoundedCornerShape(8.dp),
    medium = RoundedCornerShape(12.dp),
    large = RoundedCornerShape(16.dp)
)

@Composable
fun PharmaAutoTheme(
    darkTheme: Boolean = isSystemInDarkTheme(),
    content: @Composable () -> Unit
) {
    MaterialTheme(
        colorScheme = if (darkTheme) DarkColors else LightColors,
        typography = Typography(),
        shapes = PharmaAutoShapes,
        content = content
    )
}
