using System;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using System.IO;

namespace Squared.Render.PSM {
	public class PSMShaderManager : ContentManager {
		public PSMShaderManager (IServiceProvider serviceProvider)
			: base (serviceProvider) {
		}
		
		protected override System.IO.Stream OpenStream (string assetName) {
			var streamName = String.Format(
				"Squared.Render.shaders.{0}.cgx", assetName
			);
			
            var assembly = this.GetType().Assembly;
            return assembly.GetManifestResourceStream(streamName);
		}
		
		protected Effect ReadEffect (string assetName) {
			var gds = ServiceProvider.GetService(typeof(IGraphicsDeviceService)) as IGraphicsDeviceService;
			
			using (var ms = new MemoryStream())
			using (var stream = OpenStream(assetName)) {
				if (stream == null)
					throw new FileNotFoundException("No shader resource named '" + assetName + "'!");
				
				stream.CopyTo(ms);
				var result = new Effect(gds.GraphicsDevice, ms.GetBuffer());
                var shaderObject = result.CurrentTechnique.Passes[0]._shaderProgram;
                
                if (false) {
                    Console.WriteLine("-- {0} --", assetName);
                    
                    for (var i = 0; i < shaderObject.AttributeCount; i++)
                        Console.WriteLine("attribute #{0} name={1}", i, shaderObject.GetAttributeName(i));
                    
                    for (var i = 0; i < shaderObject.AttributeCount; i++)
                        Console.WriteLine("attribute binding #{0} name={1}", i, shaderObject.GetAttributeBinding(i));
                    
                    for (var i = 0; i < shaderObject.UniformCount; i++)
                        Console.WriteLine("uniform #{0} name={1}", i, shaderObject.GetUniformName(i));
                    
                    for (var i = 0; i < shaderObject.UniformCount; i++)
                        Console.WriteLine("uniform binding #{0} name={1}", i, shaderObject.GetUniformBinding(i));
                } else {
                    for (var i = 0; i < shaderObject.AttributeCount; i++)
                        shaderObject.SetAttributeBinding(i, shaderObject.GetAttributeName(i));
                    
                    for (var i = 0; i < shaderObject.UniformCount; i++)
                        shaderObject.SetUniformBinding(i, shaderObject.GetUniformName(i));                    
                }
                
				return result;
			}
		}
		
		public override T Load<T> (string assetName) {
			if (typeof (T) != typeof (Effect))
				throw new NotImplementedException();
			
			return (T)(object)ReadEffect(assetName);
		}
	}
}

