package com.iori.playback;

import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;
import android.util.Log;

import java.nio.charset.StandardCharsets;
import java.security.KeyStore;

import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

/**
 * API キーのような秘密の文字を、端末の鍵(Android Keystore)で暗号化する。鍵は端末の外に出せないので、
 * 暗号化した文字を持ち出しても他の端末では読めない。Unity 側(SecretStore.cs)が、返した文字をアプリの中のファイルに置く。
 * 形: Base64( IV の長さ 1 バイト + IV + AES-GCM の暗号文 )。
 */
public final class SecretBox {
    private static final String TAG = "IoriSecretBox";
    private static final String STORE = "AndroidKeyStore";
    private static final String ALIAS = "iori.secrets";

    private SecretBox() {}

    private static SecretKey key() throws Exception {
        KeyStore store = KeyStore.getInstance(STORE);
        store.load(null);
        KeyStore.Entry entry = store.getEntry(ALIAS, null);
        if (entry instanceof KeyStore.SecretKeyEntry) return ((KeyStore.SecretKeyEntry) entry).getSecretKey();
        KeyGenerator generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, STORE);
        generator.init(new KeyGenParameterSpec.Builder(ALIAS, KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setKeySize(256)
                .build());
        return generator.generateKey();
    }

    /** 暗号化する。できなければ null。 */
    public static String seal(String plain) {
        try {
            Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
            cipher.init(Cipher.ENCRYPT_MODE, key());
            byte[] iv = cipher.getIV();
            byte[] sealed = cipher.doFinal(plain.getBytes(StandardCharsets.UTF_8));
            byte[] out = new byte[1 + iv.length + sealed.length];
            out[0] = (byte) iv.length;
            System.arraycopy(iv, 0, out, 1, iv.length);
            System.arraycopy(sealed, 0, out, 1 + iv.length, sealed.length);
            return Base64.encodeToString(out, Base64.NO_WRAP);
        } catch (Exception e) {
            Log.w(TAG, "seal failed: " + e);
            return null;
        }
    }

    /** 元に戻す。できなければ(別の端末の物、壊れた物)null。 */
    public static String open(String sealed) {
        try {
            byte[] in = Base64.decode(sealed, Base64.NO_WRAP);
            int ivLength = in[0] & 0xFF;
            if (ivLength <= 0 || in.length < 1 + ivLength) return null;
            Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
            cipher.init(Cipher.DECRYPT_MODE, key(), new GCMParameterSpec(128, in, 1, ivLength));
            byte[] plain = cipher.doFinal(in, 1 + ivLength, in.length - 1 - ivLength);
            return new String(plain, StandardCharsets.UTF_8);
        } catch (Exception e) {
            Log.w(TAG, "open failed: " + e);
            return null;
        }
    }
}
