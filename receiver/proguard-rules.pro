# WireGuard Go backend: JNI entry points and service started by class name.
-keep class com.wireguard.android.backend.GoBackend { *; }
-keep class com.wireguard.android.backend.GoBackend$VpnService { *; }
-keepclassmembers class com.wireguard.android.backend.GoBackend {
    native <methods>;
}
-keep class com.wireguard.android.util.SharedLibraryLoader { *; }
