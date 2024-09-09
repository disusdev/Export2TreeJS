# Export2ThreeJS

Export2ThreeJS is a Unity script that allows you to convert Unity scenes from YAML format to Three.js JSON scenes.

## Getting Started

### How to Export a Unity Scene

1. **Export Scene in Unity:**
   - Right-click on any scene in the Unity Editor.
   - Select **TreeJS > Export2JSON** from the context menu.
   - A JSON file of the scene will be created in the `Build/` folder at the root of your project.

### Viewing the Exported Scene

#### **Option 1: View in the Three.js Online Editor**

1. Go to the [Three.js Editor](https://threejs.org/editor/).
2. Drag and drop your exported JSON file into the editor to view and interact with your scene.

#### **Option 2: View Locally with a Node.js Server**

1. **Create HTML File in Unity:**
   - In the Unity Editor, go to **Tools > TreeJS > Create HTML**. This will generate an HTML file in the `Build/` folder, allowing you to easily view the exported scene.

2. **Install [Node.js](https://nodejs.org/):** Ensure Node.js is installed on your system.

3. **Set Up and Run the Server:**

   Open your terminal, navigate to the `Build/` folder, and run the following commands:

   ```bash
   # Install Three.js
   npm install --save three
   
   # Install Vite (a fast frontend tool for web development)
   npm install --save-dev vite
   
   # Start the development server
   npx vite
   ```

4. If the server starts successfully, you'll see a URL like http://localhost:5173 in your terminal. Open this URL in your web browser.

5. Drag and drop your exported JSON file into the browser window to view and interact with your scene.