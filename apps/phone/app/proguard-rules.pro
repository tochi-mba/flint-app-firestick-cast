# R8 configuration for the phone app.
#
# This is the only place in the repository where R8 actually runs: scripts/package.ps1 only ever
# assembles the receiver in debug, so the receiver's rules have never been exercised. Everything
# here is therefore written from what the code does rather than copied from a working build.
#
# The app has no reflection of its own. Three things underneath it do.

# BouncyCastle, reached through :protocol's ADB identity handling, registers providers and resolves
# algorithm implementations by name. R8 cannot see any of those references.
-keep class org.bouncycastle.** { *; }
-dontwarn org.bouncycastle.**

# JSSE provider lookup. KeyManagerFactory, TrustManagerFactory and KeyStore are all resolved through
# Provider.Service entries keyed by string, so the implementation classes have no incoming reference
# R8 can follow.
-keep class * extends java.security.Provider { *; }
-keepclassmembers class * extends javax.net.ssl.KeyManagerFactorySpi { <init>(...); }
-keepclassmembers class * extends javax.net.ssl.TrustManagerFactorySpi { <init>(...); }
-keepclassmembers class * extends java.security.KeyStoreSpi { <init>(...); }

# Kotlin's own metadata, which coroutines reads to resume a continuation across a suspension point.
-keepclassmembers class kotlin.coroutines.jvm.internal.BaseContinuationImpl {
    java.lang.Object invokeSuspend(java.lang.Object);
}

# Compose keeps nothing by reflection, but its composer-lowered lambdas are singletons that R8's
# class merging has historically mishandled in release builds. The rule is cheap and the failure it
# prevents is a blank screen in a signed build that debug never reproduces.
-keep class androidx.compose.runtime.** { *; }
-dontwarn androidx.compose.**

# The wire types are data classes compared by value and never named by string, so nothing is kept for
# :protocol on purpose. If a future message is ever resolved reflectively, this is the comment that
# should stop somebody adding a blanket keep for the whole package instead.
