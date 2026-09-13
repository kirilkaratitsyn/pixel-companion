package dev.pixelcompanion.app;

import android.app.Activity;
import android.content.SharedPreferences;
import android.graphics.Color;
import android.net.Uri;
import android.os.Bundle;
import android.view.View;
import android.view.WindowManager;
import android.webkit.JavascriptInterface;
import android.webkit.WebResourceError;
import android.webkit.WebResourceRequest;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.Button;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;
import org.json.JSONObject;
import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;
import java.net.InterfaceAddress;
import java.net.NetworkInterface;
import java.nio.charset.StandardCharsets;
import java.util.Enumeration;
import java.util.LinkedHashSet;
import java.util.Set;

public final class MainActivity extends Activity {
    private WebView web;
    private String origin;
    private SharedPreferences preferences;
    private boolean destroyed;

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        preferences = getSharedPreferences("connection", MODE_PRIVATE);
        getWindow().setStatusBarColor(Color.BLACK); getWindow().setNavigationBarColor(Color.BLACK);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        WindowManager.LayoutParams attributes = getWindow().getAttributes();
        attributes.layoutInDisplayCutoutMode = WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES;
        getWindow().setAttributes(attributes);
        String saved = preferences.getString("origin", "");
        if (!saved.isEmpty()) connect(saved); else setup("");
    }
    private void immersive() {
        getWindow().getDecorView().setSystemUiVisibility(View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY | View.SYSTEM_UI_FLAG_FULLSCREEN | View.SYSTEM_UI_FLAG_HIDE_NAVIGATION | View.SYSTEM_UI_FLAG_LAYOUT_STABLE | View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN | View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION);
    }
    @Override public void onWindowFocusChanged(boolean focus) { super.onWindowFocusChanged(focus); if (focus) immersive(); }
    private int dp(float value) { return Math.round(value * getResources().getDisplayMetrics().density); }
    private TextView text(String value, int size) { TextView view = new TextView(this); view.setText(value); view.setTextColor(Color.rgb(206,218,208)); view.setTextSize(size); view.setPadding(0, dp(6), 0, dp(6)); return view; }
    private void setup(String message) {
        if (web != null) { web.removeJavascriptInterface("PixelNative"); web.destroy(); web = null; }
        WindowManager.LayoutParams attributes = getWindow().getAttributes(); attributes.screenBrightness = .3f; getWindow().setAttributes(attributes);
        ScrollView scroll = new ScrollView(this); scroll.setBackgroundColor(Color.BLACK);
        LinearLayout layout = new LinearLayout(this); layout.setOrientation(LinearLayout.VERTICAL); layout.setPadding(dp(32),dp(20),dp(32),dp(20)); scroll.addView(layout);
        layout.addView(text("Pixel Companion", 28)); layout.addView(text("Запустите программу на Windows. Телефон и компьютер должны быть в одной локальной сети.", 14));
        TextView status = text(message, 14); layout.addView(status);
        EditText address = new EditText(this); address.setSingleLine(true); address.setTextColor(Color.WHITE); address.setHintTextColor(Color.GRAY); address.setHint("192.168.1.100:8765"); address.setText(preferences.getString("origin", "").replace("http://", "")); address.setInputType(android.text.InputType.TYPE_CLASS_TEXT | android.text.InputType.TYPE_TEXT_VARIATION_URI); layout.addView(address);
        LinearLayout buttons = new LinearLayout(this);
        Button find = new Button(this); find.setText("Найти компьютер"); buttons.addView(find);
        Button open = new Button(this); open.setText("Подключить"); buttons.addView(open); layout.addView(buttons);
        layout.addView(text("Можно ввести адрес из окна Windows вручную. Затем приложение попросит одноразовый код.", 12));
        find.setOnClickListener(v -> { find.setEnabled(false); status.setText("Поиск компьютера…"); new Thread(() -> discover(address,status,find),"pixel-discovery").start(); });
        open.setOnClickListener(v -> { String candidate = normalize(address.getText().toString()); if (candidate == null) status.setText("Введите локальный IPv4-адрес, например 192.168.1.100:8765"); else connect(candidate); });
        setContentView(scroll); immersive();
    }
    private static String normalize(String input) {
        try {
            String value = input.trim(); if (!value.startsWith("http://")) value = "http://" + value;
            Uri uri = Uri.parse(value); String host = uri.getHost(); int port = uri.getPort() == -1 ? 8765 : uri.getPort();
            if (!"http".equals(uri.getScheme()) || host == null || !host.matches("[0-9.]+") || port < 1025 || port > 65535 || uri.getUserInfo() != null) return null;
            String[] bits = host.split("\\."); if (bits.length != 4) return null; int[] b = new int[4];
            for(int i=0;i<4;i++){b[i]=Integer.parseInt(bits[i]);if(b[i]<0||b[i]>255)return null;}
            if (!(b[0]==10 || b[0]==192&&b[1]==168 || b[0]==172&&b[1]>=16&&b[1]<=31 || b[0]==127)) return null;
            return "http://" + host + ":" + port;
        } catch (Exception e) { return null; }
    }
    private void connect(String candidate) {
        String checked = normalize(candidate); if (checked == null) { setup("Сохранённый адрес недопустим"); return; }
        origin = checked; preferences.edit().putString("origin",origin).apply();
        if (web != null) web.destroy();
        web = new WebView(this); web.setBackgroundColor(Color.BLACK);
        web.getSettings().setJavaScriptEnabled(true); web.getSettings().setDomStorageEnabled(true);
        web.getSettings().setAllowFileAccess(false); web.getSettings().setAllowContentAccess(false);
        web.getSettings().setMixedContentMode(android.webkit.WebSettings.MIXED_CONTENT_NEVER_ALLOW);
        web.addJavascriptInterface(new NativeDisplay(), "PixelNative");
        web.setWebViewClient(new WebViewClient() {
            @Override public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) { return !sameOrigin(request.getUrl()); }
            @Override public void onReceivedError(WebView view, WebResourceRequest request, WebResourceError error) {
                if (request.isForMainFrame()) runOnUiThread(() -> { if (!destroyed && view == web) setup("Компьютер недоступен. Проверьте адрес, Wi-Fi и запуск программы на Windows."); });
            }
            @Override public void onPageFinished(WebView view, String url) { immersive(); }
        });
        setContentView(web); immersive(); web.loadUrl(origin + "/");
    }
    private boolean sameOrigin(Uri uri) { return origin != null && origin.equals(uri.getScheme() + "://" + uri.getHost() + ":" + (uri.getPort()==-1?80:uri.getPort())); }
    private final class NativeDisplay {
        @JavascriptInterface public void display(boolean blank, double brightness) {
            if (Double.isNaN(brightness) || Double.isInfinite(brightness)) return;
            runOnUiThread(() -> {
                if (destroyed || web == null || web.getUrl() == null || !sameOrigin(Uri.parse(web.getUrl()))) return;
                WindowManager.LayoutParams attributes = getWindow().getAttributes();
                attributes.screenBrightness = blank ? .01f : (float)Math.max(.02,Math.min(.6,brightness)); getWindow().setAttributes(attributes);
            });
        }
    }
    private void discover(EditText address, TextView status, Button button) {
        String result = null, name = null;
        try (DatagramSocket socket = new DatagramSocket()) {
            socket.setBroadcast(true); socket.setSoTimeout(3000);
            Set<InetAddress> targets = new LinkedHashSet<>(); targets.add(InetAddress.getByName("255.255.255.255"));
            Enumeration<NetworkInterface> interfaces = NetworkInterface.getNetworkInterfaces();
            while (interfaces.hasMoreElements()) for (InterfaceAddress ia : interfaces.nextElement().getInterfaceAddresses()) if (ia.getBroadcast()!=null) targets.add(ia.getBroadcast());
            byte[] request = "PIXEL_COMPANION_DISCOVER/1".getBytes(StandardCharsets.UTF_8);
            for (InetAddress target : targets) try { socket.send(new DatagramPacket(request,request.length,target,8764)); } catch(Exception ignored) { }
            byte[] data = new byte[1024]; DatagramPacket reply = new DatagramPacket(data,data.length); socket.receive(reply);
            JSONObject info = new JSONObject(new String(data,0,reply.getLength(),StandardCharsets.UTF_8));
            if(info.getInt("version")==1){result=normalize(reply.getAddress().getHostAddress()+":"+info.getInt("port"));name=info.optString("name","Компьютер");}
        } catch(Exception ignored) { }
        final String found=result, computer=name;
        runOnUiThread(() -> { if(destroyed)return; button.setEnabled(true); if(found==null)status.setText("Не найден. Введите адрес из окна Windows вручную.");else {address.setText(found.replace("http://",""));status.setText("Найден: "+computer);} });
    }
    @Override public void onBackPressed() { setup(""); }
    @Override protected void onResume() { super.onResume(); if(web!=null)web.onResume(); immersive(); }
    @Override protected void onPause() { if(web!=null)web.onPause(); super.onPause(); }
    @Override protected void onDestroy() { destroyed=true; if(web!=null)web.destroy(); super.onDestroy(); }
}
