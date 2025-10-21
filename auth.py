from azure.identity import InteractiveBrowserCredential
import requests
import os

# Your application ID
client_id = "63696678-8070-4259-91d9-292979db05c4"

# Interactive Browser Credential with redirect URI for native app
# This will open a browser window for you to sign in
credential = InteractiveBrowserCredential(
    client_id=client_id,
    redirect_uri="http://localhost:8400"
)


scope = "ac180c33-bd40-461a-bbfd-1a4ff964e8a0/.default"  # Your specific service scope

try:
    token = credential.get_token(scope)
    print("✅ Authentication successful!")
    print("Access Token:", token.token[:40] + "...")
    print("Token expires at:", token.expires_on)
    
    # Now you can use this token to call your APIs
    print("\n🔑 Token ready for API calls!")
    
except Exception as e:
    print(f"❌ Authentication failed: {e}")
    print("Make sure your app registration has the necessary permissions and admin consent.")
