#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;

using System;
using System.IO;
using System.Collections.Generic;
using YamlDotNet.RepresentationModel;
using System.Linq;

namespace TreeJS
{
    public static class Matrix4x4Extensions
    {
        public static float[] ToFloatArray(this Matrix4x4 matrix)
        {
            return new float[16]
            {
                matrix.m00, matrix.m10, matrix.m20, matrix.m30,
                matrix.m01, matrix.m11, matrix.m21, matrix.m31,
                matrix.m02, matrix.m12, matrix.m22, matrix.m32,
                matrix.m03, matrix.m13, -matrix.m23, matrix.m33
            };
        }
    }

    public static class SceneExporter
    {
        const string UnityTagPrefix = "tag:unity3d.com,2011:";

        enum UnityComponent
        {
            GAME_OBJECT = 1,
            TRANSFORM = 4,
            CAMERA = 20,
            MESH_RENDERER = 23,
            MESH_FILTER = 33,
            LIGHT = 108
        }

        enum TreeTypes
        {
            BoxGeometry,
            MeshPhongMaterial,
            Mesh,
            PerspectiveCamera,
            DirectionalLight
        };

        class GameObjectInfo
        {
            public string Name;
            public TransformInfo Transform;
            public CameraInfo Camera;
            public LightInfo Light;
        }

        class TransformInfo
        {
            public float[] Position;
            public float[] Rotation;
            public float[] Scale;
            public int m_GameObject;
            public List<int> Children = new List<int>();
            public int m_Father;

            public Matrix4x4 GetMat4()
            {
                return Matrix4x4.TRS(
                    new Vector3(Position[0], Position[1], Position[2]),
                    Quaternion.Euler(
                        Rotation[0] * Mathf.Rad2Deg, 
                        Rotation[1] * Mathf.Rad2Deg, 
                        Rotation[2] * Mathf.Rad2Deg
                    ),
                    new Vector3(Scale[0], Scale[1], Scale[2])
                );
            }
        }

        class CameraInfo
        {
            public float FieldOfView;
            public int m_GameObject;
        }

        class LightInfo
        {
            public int m_GameObject;
        }

        class SceneInfo
        {
            public Dictionary<int, GameObjectInfo> gameObjects = 
                new Dictionary<int, GameObjectInfo>();
            public Dictionary<int, TransformInfo> transforms = 
                new Dictionary<int, TransformInfo>();
            public Dictionary<int, CameraInfo> cameras = 
                new Dictionary<int, CameraInfo>();
            public Dictionary<int, LightInfo> lights = 
                new Dictionary<int, LightInfo>();
        }

        [Serializable]
        struct Metadata
        {
            public double version;
            public string type;
            public string generator;
        }

        [Serializable]
        struct Geometry
        {
            public string uuid;
            public string type;
            public double width;
            public double height;
            public double depth;
            public int widthSegments;
            public int heightSegments;
            public int depthSegments;
        }

        [Serializable]
        struct Material
        {
            public string uuid;
            public string type;
            public int color;
            public double reflectivity;
            public double refractionRatio;
            public bool flatShading;
            public bool vertexColors;
            public double shininess;
        }

        [Serializable]
        struct TreeJSObject
        {
            public string uuid;
            public string type;
            public float[] matrix;
            public float fov;
            public string geometry;
            public string material;
            public bool castShadow;
            public bool receiveShadow;
            public List<TreeJSObject> children;
        }

        [Serializable]
        struct TreeJSScene
        {
            public string uuid;
            public string type;
            public List<TreeJSObject> children;
        }

        [Serializable]
        struct Scene
        {
            public Metadata metadata;
            public List<Geometry> geometries;
            public List<Material> materials;
            public TreeJSScene @object;
        }

        [MenuItem("Assets/TreeJS/Export2JSON", true)]
        private static bool ValidateContextMenu()
        {
            return Selection.activeObject && 
                   Selection.activeObject is SceneAsset;
        }

        [MenuItem("Assets/TreeJS/Export2JSON", false, 0)]
        private static void Export2JSON()
        {
            string scenePath = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (EditorUtility.DisplayDialog(
                "Export Scene", 
                $"Do you want to export the scene '{scenePath}'?", 
                "Yes", "No"))
            {
                if (File.Exists(scenePath))
                {
                    var gameObjects = ReadUnityScene(File.ReadAllText(scenePath));
                    string json = Export(gameObjects);
                    if (!Directory.Exists("Build"))
                    {
                        Directory.CreateDirectory("Build");
                    }
                    File.WriteAllText(
                        $"Build/{Path.GetFileName(scenePath)}.json", 
                        json);
                    Debug.Log($"[Export2TreeJS] {Path.GetFileName(scenePath)}.json created in Build/ folder.");
                }
                else
                {
                    Debug.LogError($"File not found: {scenePath}");
                }
            }
        }

        private static Dictionary<string, object> ConvertYamlNodeToDictionary(
            YamlNode node)
        {
            var dictionary = new Dictionary<string, object>();

            if (node is YamlMappingNode mappingNode)
            {
                foreach (var entry in mappingNode.Children)
                {
                    var key = ((YamlScalarNode)entry.Key).Value;
                    var valueNode = entry.Value;

                    if (valueNode is YamlMappingNode nestedMapping)
                    {
                        dictionary[key] = ConvertYamlNodeToDictionary(nestedMapping);
                    }
                    else if (valueNode is YamlScalarNode scalarNode)
                    {
                        dictionary[key] = scalarNode.Value;
                    }
                    else
                    {
                        dictionary[key] = valueNode.ToString();
                    }
                }
            }

            return dictionary;
        }

        static SceneInfo ReadUnityScene(string yaml)
        {
            var yamlStream = new YamlStream();
            yamlStream.Load(new StringReader(yaml));

            SceneInfo scene = new SceneInfo();

            foreach (var document in yamlStream.Documents)
            {
                var rootNode = (YamlMappingNode)document.RootNode;

                foreach (var entry in rootNode.Children)
                {
                    var componentTag = (UnityComponent)int.Parse(
                        rootNode.Tag.Value.Replace(UnityTagPrefix, ""));
                    var anchor = int.Parse(rootNode.Anchor.Value);
                    var node = (YamlMappingNode)entry.Value;

                    switch (componentTag)
                    {
                        case UnityComponent.GAME_OBJECT:
                            var fileID = anchor;
                            var name = 
                                node.Children[new YamlScalarNode("m_Name")]
                                    .ToString();

                            scene.gameObjects[fileID] = new GameObjectInfo
                            {
                                Name = name,
                                Transform = null
                            };
                            break;

                        case UnityComponent.TRANSFORM:
                            HandleTransform(node, anchor, scene);
                            break;

                        case UnityComponent.CAMERA:
                            var cameraFileID = anchor;
                            var fov = float.Parse(
                                node.Children[new YamlScalarNode(
                                    "field of view")].ToString());
                            var gameObjectNode = (YamlMappingNode)
                                node.Children[new YamlScalarNode(
                                    "m_GameObject")];

                            scene.cameras[cameraFileID] = new CameraInfo
                            {
                                FieldOfView = fov,
                                m_GameObject = int.Parse(
                                    gameObjectNode.Children[new 
                                        YamlScalarNode("fileID")]
                                        .ToString()),
                            };
                            break;

                        case UnityComponent.LIGHT:
                            var lightFileID = anchor;
                            var lightGameObjectNode = (YamlMappingNode)
                                node.Children[new YamlScalarNode(
                                    "m_GameObject")];

                            scene.lights[lightFileID] = new LightInfo
                            {
                                m_GameObject = int.Parse(
                                    lightGameObjectNode.Children[new 
                                        YamlScalarNode("fileID")]
                                        .ToString()),
                            };
                            break;
                    }
                }
            }

            ProcessTransformHierarchy(scene);
            LinkComponents(scene);

            return scene;
        }

        private static void HandleTransform(YamlMappingNode node, 
                                            int anchor, 
                                            SceneInfo scene)
        {
            var positionNode = (YamlMappingNode)
                node.Children[new YamlScalarNode("m_LocalPosition")];
            var rotationNode = (YamlMappingNode)
                node.Children[new YamlScalarNode("m_LocalRotation")];
            var scaleNode = (YamlMappingNode)
                node.Children[new YamlScalarNode("m_LocalScale")];
            var gameObjectNode = (YamlMappingNode)
                node.Children[new YamlScalarNode("m_GameObject")];
            var fatherNode = (YamlMappingNode)
                node.Children[new YamlScalarNode("m_Father")];

            List<int> childrenIDs = new List<int>();
            if (node.Children.ContainsKey(new YamlScalarNode("m_Children")))
            {
                var childrensNode = (YamlSequenceNode)
                    node.Children[new YamlScalarNode("m_Children")];
                foreach (var child in childrensNode)
                {
                    var childMapping = (YamlMappingNode)child;
                    if (childMapping.Children.ContainsKey(new 
                        YamlScalarNode("fileID")))
                    {
                        int childID = int.Parse(childMapping.Children[
                            new YamlScalarNode("fileID")].ToString());
                        if (childID == 0) continue;
                        childrenIDs.Add(childID);
                    }
                }
            }

            scene.transforms[anchor] = new TransformInfo
            {
                Position = new float[]
                {
                    float.Parse(positionNode.Children[
                        new YamlScalarNode("x")].ToString()),
                    float.Parse(positionNode.Children[
                        new YamlScalarNode("y")].ToString()),
                    float.Parse(positionNode.Children[
                        new YamlScalarNode("z")].ToString())
                },
                Rotation = new float[]
                {
                    float.Parse(rotationNode.Children[
                        new YamlScalarNode("x")].ToString()),
                    float.Parse(rotationNode.Children[
                        new YamlScalarNode("y")].ToString()),
                    float.Parse(rotationNode.Children[
                        new YamlScalarNode("z")].ToString())
                },
                Scale = new float[]
                {
                    float.Parse(scaleNode.Children[
                        new YamlScalarNode("x")].ToString()),
                    float.Parse(scaleNode.Children[
                        new YamlScalarNode("y")].ToString()),
                    float.Parse(scaleNode.Children[
                        new YamlScalarNode("z")].ToString())
                },
                m_GameObject = int.Parse(
                    gameObjectNode.Children[new YamlScalarNode("fileID")]
                        .ToString()),
                m_Father = fatherNode.Children.Count > 0 ? int.Parse(
                    fatherNode.Children[new YamlScalarNode("fileID")]
                        .ToString()) : -1,
                Children = childrenIDs
            };
        }

        private static void ProcessTransformHierarchy(SceneInfo scene)
        {
            foreach (var transform in scene.transforms.Values)
            {
                if (transform.m_Father != -1 && 
                    scene.transforms.TryGetValue(
                        transform.m_Father, out TransformInfo parent))
                {
                    parent.Children.Add(transform.m_GameObject);
                }
            }
        }

        private static void LinkComponents(SceneInfo scene)
        {
            foreach (var camera in scene.cameras.Values)
            {
                if (scene.gameObjects.TryGetValue(camera.m_GameObject, 
                    out GameObjectInfo go))
                {
                    go.Camera = camera;
                }
            }

            foreach (var light in scene.lights.Values)
            {
                if (scene.gameObjects.TryGetValue(light.m_GameObject, 
                    out GameObjectInfo go))
                {
                    go.Light = light;
                }
            }

            foreach (var transform in scene.transforms.Values)
            {
                if (scene.gameObjects.TryGetValue(transform.m_GameObject, 
                    out GameObjectInfo go))
                {
                    go.Transform = transform;
                }
            }
        }

        static string Export(SceneInfo scene)
        {
            Scene exportScene = new Scene
            {
                metadata = new Metadata 
                { 
                    version = 4.5, 
                    type = "Object", 
                    generator = "SceneExporter" 
                },
                geometries = new List<Geometry>
                {
                    new Geometry
                    {
                        uuid = Guid.NewGuid().ToString(),
                        type = TreeTypes.BoxGeometry.ToString(),
                        width = 1,
                        height = 1,
                        depth = 1,
                        widthSegments = 1,
                        heightSegments = 1,
                        depthSegments = 1
                    }
                },
                materials = new List<Material>
                {
                    new Material
                    {
                        uuid = Guid.NewGuid().ToString(),
                        type = TreeTypes.MeshPhongMaterial.ToString(),
                        color = 16777215,
                        reflectivity = 1,
                        refractionRatio = 0.98,
                        flatShading = false,
                        vertexColors = false,
                        shininess = 30
                    }
                },
                @object = new TreeJSScene
                {
                    uuid = Guid.NewGuid().ToString(),
                    type = "Scene",
                    children = new List<TreeJSObject>()
                }
            };

            foreach (var go in scene.gameObjects.Values)
            {
                TreeJSObject newObject = new TreeJSObject
                {
                    uuid = Guid.NewGuid().ToString(),
                    type = go.Camera != null ? 
                        TreeTypes.PerspectiveCamera.ToString() : 
                        go.Light != null ? 
                        TreeTypes.DirectionalLight.ToString() : 
                        TreeTypes.Mesh.ToString(),
                    matrix = go.Transform?.GetMat4().ToFloatArray(),
                    fov = go.Camera != null ? go.Camera.FieldOfView : 0,
                    geometry = exportScene.geometries[0].uuid,
                    material = exportScene.materials[0].uuid,
                    castShadow = go.Light != null,
                    receiveShadow = go.Light != null,
                    children = go.Transform?.Children.Select(child => 
                        new TreeJSObject
                    {
                        uuid = Guid.NewGuid().ToString(),
                        type = TreeTypes.Mesh.ToString(),
                        matrix = go.Transform?.GetMat4().ToFloatArray(),
                        geometry = exportScene.geometries[0].uuid,
                        material = exportScene.materials[0].uuid
                    }).ToList() ?? new List<TreeJSObject>()
                };

                exportScene.@object.children.Add(newObject);
            }

            return JsonUtility.ToJson(exportScene, true);
        }
        
        [MenuItem("Tools/TreeJS/Create HTML")]
        public static void CreateHTML()
        {
            string html = @"
            <!DOCTYPE html>
            <html lang='en'>
            <head>
                <meta charset='utf-8'>
                <title>ExportedSceneFromUnity</title>
                <style>
                    body {
                        margin: 0;
                        align-items: center;
                        background: #282c34;
                        font-family: 'Arial', sans-serif;
                        overflow: hidden;
                    }
                    h1 {
                        font-size: 2rem;
                        font-weight: bold;
                        text-align: center;
                        color: transparent;
                        background: linear-gradient(45deg, #00c6ff, #0072ff);
                        -webkit-background-clip: text;
                        background-clip: text;
                        text-shadow: 2px 4px 10px rgba(0, 0, 0, 0.4);
                        margin: 0;
                        padding: 10px;
                        user-select: none;
                        transition: transform 0.3s ease;
                    }
                    h1:hover {
                        transform: scale(1.05);
                    }
                </style>
            </head>
            <body>
                <script type='module'>
                    import * as THREE from 'three';
                    import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
                    
                    const scene = new THREE.Scene();
                    const clock = new THREE.Clock();
                    let camera, controls, renderer;
                    let light1, light2, light3, light4;
                    
                    init();
                    
                    function init() {
                        renderer = new THREE.WebGLRenderer({ antialias: true });
                        renderer.setClearColor(0x000000);
                        renderer.setPixelRatio(window.devicePixelRatio);
                        renderer.setSize(window.innerWidth, window.innerHeight);
                        renderer.shadowMap.enabled = true;
                        renderer.shadowMap.type = THREE.PCFSoftShadowMap;
                        renderer.setAnimationLoop(animate);
                        
                        document.body.appendChild(renderer.domElement);
                        window.addEventListener('resize', onWindowResize);
                        document.addEventListener('dragover', function(event) {
                            event.preventDefault();
                            event.dataTransfer.dropEffect = 'copy';
                        });
                        document.addEventListener('drop', function(event) {
                            event.preventDefault();
                            if (event.dataTransfer.types[0] === 'text/plain') return;
                            if (event.dataTransfer.items) {
                                for (let i = 0; i < event.dataTransfer.items.length; i++) {
                                    const item = event.dataTransfer.items[i];
                                    if (item.kind === 'file' && item.type === 'application/json') {
                                        const file = item.getAsFile();
                                        if (file) {
                                            loadScene(file);
                                        }
                                    }
                                }
                            } else {
                                for (let i = 0; i < event.dataTransfer.files.length; i++) {
                                    const file = event.dataTransfer.files[i];
                                    if (file.type === 'application/json') {
                                        loadScene(file);
                                    }
                                }
                            }
                        });
                        
                        const sphere = new THREE.SphereGeometry(0.2, 8, 8);
                        light1 = new THREE.PointLight(0xff0040, 400);
                        light1.add(new THREE.Mesh(sphere, new THREE.MeshBasicMaterial({ color: 0xff0040 })));
                        scene.add(light1);
                        light2 = new THREE.PointLight(0x0040ff, 400);
                        light2.add(new THREE.Mesh(sphere, new THREE.MeshBasicMaterial({ color: 0x0040ff })));
                        scene.add(light2);
                        light3 = new THREE.PointLight(0x80ff80, 400);
                        light3.add(new THREE.Mesh(sphere, new THREE.MeshBasicMaterial({ color: 0x80ff80 })));
                        scene.add(light3);
                        light4 = new THREE.PointLight(0xffaa00, 400);
                        light4.add(new THREE.Mesh(sphere, new THREE.MeshBasicMaterial({ color: 0xffaa00 })));
                        scene.add(light4);
                    }
                    
                    function onWindowResize() {
                        if (camera) {
                            camera.aspect = window.innerWidth / window.innerHeight;
                            camera.updateProjectionMatrix();
                        }
                        renderer.setSize(window.innerWidth, window.innerHeight);
                    }
                    
                    function loadScene(file) {
                        loadSceneFromFile(file.name);
                    }
                    
                    function loadSceneFromFile(url) {
                        fetch(url)
                            .then(response => response.json())
                            .then(json => {
                                console.log(json);
                                const loader = new THREE.ObjectLoader();
                                const loadedScene = loader.parse(json);
                                camera = loadedScene.children.find(child => child.type === 'PerspectiveCamera' || 
                                                                            child.type === 'OrthographicCamera');
                                camera.aspect = window.innerWidth / window.innerHeight;
                                camera.updateProjectionMatrix();
                                controls = new OrbitControls(camera, renderer.domElement);
                                controls.listenToKeyEvents(window);
                                controls.enableDamping = true;
                                controls.dampingFactor = 0.05;
                                controls.screenSpacePanning = false;
                                controls.minDistance = 10;
                                controls.maxDistance = 50;
                                controls.maxPolarAngle = Math.PI / 2;
                                scene.add(loadedScene);
                            })
                            .catch(error => {
                                console.error('Error loading the scene JSON:', error);
                            });
                    }
                    
                    function animate() {
                        const time = Date.now() * 0.0005;
                        light1.position.x = Math.sin(time * 0.7) * 3;
                        light1.position.y = Math.cos(time * 0.5) * 4;
                        light1.position.z = Math.cos(time * 0.3) * 3;
                        light2.position.x = Math.cos(time * 0.3) * 3;
                        light2.position.y = Math.sin(time * 0.5) * 4;
                        light2.position.z = Math.sin(time * 0.7) * 3;
                        light3.position.x = Math.sin(time * 0.7) * 3;
                        light3.position.y = Math.cos(time * 0.3) * 4;
                        light3.position.z = Math.sin(time * 0.5) * 3;
                        light4.position.x = Math.sin(time * 0.3) * 3;
                        light4.position.y = Math.cos(time * 0.7) * 4;
                        light4.position.z = Math.sin(time * 0.5) * 3;
                        if (camera) {
                            controls.update();
                            renderer.render(scene, camera);
                        }
                    }
                </script>
                <h1>Drop three.js Scene</h1>
            </body>
            </html>";
            
            if (!Directory.Exists("Build"))
            {
                Directory.CreateDirectory("Build");
            }
            File.WriteAllText(
                        $"Build/index.html", 
                        html);
            
            Debug.Log("[Export2TreeJS] index.html created in Build/ folder.");
        }
    }
}

#endif
