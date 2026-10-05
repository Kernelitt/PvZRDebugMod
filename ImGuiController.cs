using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System;

namespace DebugMenuMod
{
    public class ImGuiController : IDisposable
    {
        private int _fontTexture;
        private int _shaderProgram;
        private int _vbo, _ebo, _vao;
        private int _windowWidth;
        private int _windowHeight;
        private System.Numerics.Vector2 _scaleFactor = System.Numerics.Vector2.One;

        public event Action OnBuildUI;

        public ImGuiController(int width, int height)
        {
            _windowWidth = width;
            _windowHeight = height;

            IntPtr context = ImGui.CreateContext();
            ImGui.SetCurrentContext(context);

            var io = ImGui.GetIO();
            io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;

            InitGeometry();
            InitShaders();
            CreateFontTexture();

            ImGui.StyleColorsDark();
        }

        public void WindowResized(int width, int height)
        {
            _windowWidth = width;
            _windowHeight = height;
        }

        public void SetupInput(GameWindow w)
        {
            var io = ImGui.GetIO();

            w.TextInput += (x) => io.AddInputCharacter((uint)x.Unicode);
            w.MouseMove += (x) => io.AddMousePosEvent(x.X, x.Y);

            w.MouseDown += (x) =>
            {
                int buttonIndex = (int)x.Button;
                if (buttonIndex >= 0 && buttonIndex < 5) io.AddMouseButtonEvent(buttonIndex, true);
            };
            w.MouseUp += (x) =>
            {
                int buttonIndex = (int)x.Button;
                if (buttonIndex >= 0 && buttonIndex < 5) io.AddMouseButtonEvent(buttonIndex, false);
            };

            w.MouseWheel += (x) => io.AddMouseWheelEvent(x.OffsetX, x.OffsetY);

            w.KeyDown += (x) =>
            {
                ImGuiKey imGuiKey = TranslateKey(x.Key);
                if (imGuiKey != ImGuiKey.None) io.AddKeyEvent(imGuiKey, true);
            };
            w.KeyUp += (x) =>
            {
                ImGuiKey imGuiKey = TranslateKey(x.Key);
                if (imGuiKey != ImGuiKey.None) io.AddKeyEvent(imGuiKey, false);
            };
        }

        public void Update(GameWindow window, float deltaTime)
        {
            // Метод пустой, так как в кастомных движках стабильнее собирать кадр во время Render
        }

        public void Render()
        {
            // Готовим данные кадра непосредственно перед сборкой UI
            var io = ImGui.GetIO();
            io.DeltaTime = 1f / 60f;
            io.DisplaySize = new System.Numerics.Vector2(_windowWidth, _windowHeight);
            io.DisplayFramebufferScale = _scaleFactor;

            // Начинаем кадр
            ImGui.NewFrame();

            // Вызываем отрисовку окон дебаг-меню
            OnBuildUI?.Invoke();

            // Финализируем кадр и отправляем на отрисовку
            ImGui.Render();
            RenderDrawData(ImGui.GetDrawData());
        }

        private void RenderDrawData(ImDrawDataPtr drawData)
        {
            if (drawData.CmdListsCount == 0) return;

            // Сохраняем состояние OpenGL
            int prevProgram = GL.GetInteger(GetPName.CurrentProgram);
            int prevVAO = GL.GetInteger(GetPName.VertexArrayBinding);
            int prevTexture = GL.GetInteger(GetPName.TextureBinding2D);
            int prevArrayBuffer = GL.GetInteger(GetPName.ArrayBufferBinding);
            int prevElementArrayBuffer = GL.GetInteger(GetPName.ElementArrayBufferBinding);

            int[] lastViewport = new int[4];
            GL.GetInteger(GetPName.Viewport, lastViewport);

            GL.Enable(EnableCap.Blend);
            GL.BlendEquation(BlendEquationMode.FuncAdd);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Disable(EnableCap.CullFace);
            GL.Disable(EnableCap.DepthTest);
            GL.Enable(EnableCap.ScissorTest);

            // Настраиваем шейдер и матрицу проекции
            GL.UseProgram(_shaderProgram);
            var io = ImGui.GetIO();
            Matrix4 mvp = Matrix4.CreateOrthographicOffCenter(0.0f, io.DisplaySize.X, io.DisplaySize.Y, 0.0f, -1.0f, 1.0f);
            GL.UniformMatrix4(GL.GetUniformLocation(_shaderProgram, "projection_matrix"), false, ref mvp);

            GL.BindVertexArray(_vao);

            for (int n = 0; n < drawData.CmdListsCount; n++)
            {
                ImDrawListPtr cmdList = drawData.CmdLists[n];

                // Передаем вершины и индексы
                GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
                GL.BufferData(BufferTarget.ArrayBuffer, cmdList.VtxBuffer.Size * UnsafeSizeOf<ImDrawVert>(), cmdList.VtxBuffer.Data, BufferUsageHint.StreamDraw);

                GL.BindBuffer(BufferTarget.ElementArrayBuffer, _ebo);
                GL.BufferData(BufferTarget.ElementArrayBuffer, cmdList.IdxBuffer.Size * sizeof(ushort), cmdList.IdxBuffer.Data, BufferUsageHint.StreamDraw);

                for (int cmdIdx = 0; cmdIdx < cmdList.CmdBuffer.Size; cmdIdx++)
                {
                    ImDrawCmdPtr pcmd = cmdList.CmdBuffer[cmdIdx];

                    // Защита Scissor Box с инверсией Y под OpenGL
                    int scissorX = (int)pcmd.ClipRect.X;
                    int scissorY = (int)(_windowHeight - pcmd.ClipRect.W);
                    int scissorWidth = (int)(pcmd.ClipRect.Z - pcmd.ClipRect.X);
                    int scissorHeight = (int)(pcmd.ClipRect.W - pcmd.ClipRect.Y);

                    if (scissorWidth <= 0 || scissorHeight <= 0) continue;

                    GL.Scissor(scissorX, scissorY, scissorWidth, scissorHeight);

                    GL.BindTexture(TextureTarget.Texture2D, (int)pcmd.TextureId);
                    GL.DrawElements(PrimitiveType.Triangles, (int)pcmd.ElemCount, DrawElementsType.UnsignedShort, (IntPtr)(pcmd.IdxOffset * sizeof(ushort)));
                }
            }

            // Восстанавливаем состояние OpenGL обратно для игры
            GL.Disable(EnableCap.ScissorTest);
            GL.BindVertexArray(prevVAO);
            GL.BindBuffer(BufferTarget.ArrayBuffer, prevArrayBuffer);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, prevElementArrayBuffer);
            GL.UseProgram(prevProgram);
            GL.BindTexture(TextureTarget.Texture2D, prevTexture);
            GL.Viewport(lastViewport[0], lastViewport[1], lastViewport[2], lastViewport[3]);
        }

        private void CreateFontTexture()
        {
            var io = ImGui.GetIO();
            io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height);

            _fontTexture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, _fontTexture);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);

            io.Fonts.SetTexID((IntPtr)_fontTexture);
            io.Fonts.ClearTexData();
        }

        private void InitGeometry()
        {
            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();
            _ebo = GL.GenBuffer();

            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);

            int stride = UnsafeSizeOf<ImDrawVert>();
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, 0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, 8);
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 4, VertexAttribPointerType.UnsignedByte, true, stride, 16);
        }

        private void InitShaders()
        {
            // ИСПРАВЛЕНО: Чистые строки GLSL без комментариев внутри
            string vertexShaderSource = @"#version 330 core
            layout (location = 0) in vec2 Position;
            layout (location = 1) in vec2 UV;
            layout (location = 2) in vec4 Color;
            uniform mat4 projection_matrix;
            out vec2 Frag_UV;
            out vec4 Frag_Color;
            void main() {
                Frag_UV = UV;
                Frag_Color = Color;
                gl_Position = projection_matrix * vec4(Position.xy, 0.0, 1.0);
            }";

            string fragmentShaderSource = @"#version 330 core
            in vec2 Frag_UV;
            in vec4 Frag_Color;
            uniform sampler2D Texture;
            layout (location = 0) out vec4 Out_Color;
            void main() {
                Out_Color = Frag_Color * texture(Texture, Frag_UV.st);
            }";

            int vs = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vs, vertexShaderSource);
            GL.CompileShader(vs);

            int fs = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fs, fragmentShaderSource);
            GL.CompileShader(fs);

            _shaderProgram = GL.CreateProgram();
            GL.AttachShader(_shaderProgram, vs);
            GL.AttachShader(_shaderProgram, fs);
            GL.LinkProgram(_shaderProgram);

            GL.DeleteShader(vs);
            GL.DeleteShader(fs);
        }

        private ImGuiKey TranslateKey(Keys key)
        {
            return key switch
            {
                Keys.Tab => ImGuiKey.Tab,
                Keys.Left => ImGuiKey.LeftArrow,
                Keys.Right => ImGuiKey.RightArrow,
                Keys.Up => ImGuiKey.UpArrow,
                Keys.Down => ImGuiKey.DownArrow,
                Keys.PageUp => ImGuiKey.PageUp,
                Keys.PageDown => ImGuiKey.PageDown,
                Keys.Home => ImGuiKey.Home,
                Keys.End => ImGuiKey.End,
                Keys.Delete => ImGuiKey.Delete,
                Keys.Backspace => ImGuiKey.Backspace,
                Keys.Enter => ImGuiKey.Enter,
                Keys.Escape => ImGuiKey.Escape,
                Keys.A => ImGuiKey.A,
                Keys.C => ImGuiKey.C,
                Keys.V => ImGuiKey.V,
                Keys.X => ImGuiKey.X,
                Keys.Y => ImGuiKey.Y,
                Keys.Z => ImGuiKey.Z,
                Keys.LeftControl => ImGuiKey.LeftCtrl,
                Keys.RightControl => ImGuiKey.RightCtrl,
                Keys.LeftShift => ImGuiKey.LeftShift,
                Keys.RightShift => ImGuiKey.RightShift,
                Keys.LeftAlt => ImGuiKey.LeftAlt,
                Keys.RightAlt => ImGuiKey.RightAlt,
                _ => ImGuiKey.None
            };
        }
        private static unsafe int UnsafeSizeOf<T>() where T : unmanaged => sizeof(T);
        public void Dispose()
        {
            GL.DeleteBuffer(_vbo);
            GL.DeleteBuffer(_ebo);
            GL.DeleteVertexArray(_vao);
            GL.DeleteTexture(_fontTexture);
            GL.DeleteProgram(_shaderProgram);
            ImGui.DestroyContext();
        }
    }
}