Shader "Custom/SciFiGlassWall_v5"
{
    Properties
    {
        _EfficiencyScore  ("Efficiency Score",     Range(0,1))    = 0.75
        _ColorPositive    ("Color Positive",       Color)         = (0.161, 0.969, 0.753, 1)
        _ColorNegative    ("Color Negative",       Color)         = (1.0, 0.2, 0.306, 1)

        _GlassOpacity     ("Glass Base Opacity",   Range(0,0.15)) = 0.05
        _GlassTint        ("Glass Tint",           Color)         = (0.02, 0.035, 0.06, 1)

        _HexScale         ("Hex Scale (Tiling)",   Range(2,30))   = 8.0
        _LineWidth        ("Line Width",           Range(0.01,0.15)) = 0.04
        _LineBaseAlpha    ("Line Base Opacity",    Range(0,0.3))  = 0.06

        _PulseSpeed       ("Pulse Speed (base)",   Range(0.1,2))  = 0.45
        _PulseIntensity   ("Pulse Intensity",      Range(0,2))    = 1.1

        _EdgeWidth        ("Edge Glow Width",      Range(0,0.1))  = 0.038
        _EdgeBrightness   ("Edge Brightness",      Range(0,3))    = 1.5
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float  _EfficiencyScore;
                float4 _ColorPositive, _ColorNegative, _GlassTint;
                float  _GlassOpacity, _HexScale, _LineWidth, _LineBaseAlpha;
                float  _PulseSpeed, _PulseIntensity, _EdgeWidth, _EdgeBrightness;
            CBUFFER_END

            struct Attr { float4 pos:POSITION; float2 uv:TEXCOORD0; };
            struct Vary { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };

            // Noise helpers
            float hash2(float2 p){ p=frac(p*float2(127.1,311.7)); p+=dot(p,p+19.19); return frac(p.x*p.y); }
            float sNoise(float2 p){
                float2 i=floor(p), f=frac(p), u=f*f*(3.-2.*f);
                return lerp(lerp(hash2(i),hash2(i+float2(1,0)),u.x),
                            lerp(hash2(i+float2(0,1)),hash2(i+float2(1,1)),u.x),u.y);
            }
            float fbm(float2 p){ float v=0,a=.5; for(int i=0;i<3;i++){v+=a*sNoise(p);p*=2.1;a*=.5;} return v; }

            // ── Hex Lattice ───────────────────────────────────────────────
            // Matches Unity's Hex Lattice node output exactly.
            // uv: tiled UV. Returns: distance to nearest hex edge (0=center, 1=edge boundary).
            // cellID: unique per-cell coordinate for noise.
            float HexLattice(float2 uv, out float2 cellID)
            {
                // Unity's Hex Lattice uses this axial grid approach:
                // Scale so one hex fits in a 1x1 cell in skewed space
                // Aspect ratio: hex width = 1, hex height = sqrt(3)
                const float2 r = float2(1.0, 1.7320508); // (1, sqrt3)
                const float2 h = r * 0.5;

                // Two overlapping grids offset by half a cell
                float2 a = fmod(uv,     r) - h;   // fmod not frac = correct for negatives in HLSL
                float2 b = fmod(uv + h, r) - h;

                // Pick whichever center is closer
                float2 gv = dot(a,a) < dot(b,b) ? a : b;

                cellID = uv - gv; // integer cell center

                // Hex SDF: distance from center of a regular flat-top hexagon
                // max(|x|, |x|*0.5 + |y|*sqrt3*0.5) normalised so edge = 0.5
                float2 ag = abs(gv);
                float d = max(ag.x, ag.x * 0.5 + ag.y * 0.8660254);
                return d; // 0 at center, ~0.5 at edge
            }

            Vary vert(Attr IN)
            {
                Vary O;
                O.pos = TransformObjectToHClip(IN.pos.xyz);
                O.uv  = IN.uv;
                return O;
            }

            float4 frag(Vary IN) : SV_Target
            {
                float2 uv     = IN.uv * _HexScale;
                float  t      = _Time.y;
                float  sc     = saturate(_EfficiencyScore);
                float  danger = 1.0 - sc;
                float3 ledCol = lerp(_ColorNegative.rgb, _ColorPositive.rgb, sc);

                float speed = _PulseSpeed * (1.0 + danger * 2.0);

                // ── Hex grid ─────────────────────────────────────────────
                float2 cellID;
                float  hd = HexLattice(uv, cellID);

                // Edge: thin band where hd approaches 0.5
                float edgeDist = 0.5 - hd;
                float hexEdge  = 1.0 - smoothstep(0.0, _LineWidth, edgeDist);

                // ── Pulse waves ───────────────────────────────────────────
                float2 uvN = IN.uv; // normalised 0-1 UV for wave direction
                float w1 = pow(saturate(sin(uvN.x*3.5 - uvN.y*1.8 - t*speed*1.0)*0.5+0.5), 3.0);
                float w2 = pow(saturate(sin(uvN.x*1.2 + uvN.y*4.1 - t*speed*0.75+2.1)*0.5+0.5), 3.0);
                float w3 = pow(saturate(sin(-uvN.x*2.6 + uvN.y*2.9 - t*speed*1.3+4.4)*0.5+0.5), 4.0);

                float cellN = fbm(cellID * 0.4 + t * 0.03 * speed);
                float pulse = pow(max(max(w1,w2),w3) * (0.3 + 0.7*cellN), 1.4) * _PulseIntensity;

                // Base: always faintly visible
                float lineBase   = hexEdge * _LineBaseAlpha;
                // Active: brighter on pulse
                float lineActive = hexEdge * pulse * 1.1;
                // Wider: threshold expands during pulse
                float lineWide   = (1.0 - smoothstep(0.0, _LineWidth*(1.0+pulse*2.5), edgeDist)) * pulse * 0.4;
                float hexContrib = lineBase + lineActive + lineWide;

                // ── Edge glow (wall border) ───────────────────────────────
                float2 ed    = min(IN.uv, 1.0 - IN.uv);
                float  eMask = 1.0 - smoothstep(0.0, _EdgeWidth, min(ed.x, ed.y));
                float  eP    = 0.5 + 0.5 * sin(t * speed * 0.9);
                float  eGlow = eMask * eP * _EdgeBrightness;

                // ── Compose ───────────────────────────────────────────────
                float3 col   = _GlassTint.rgb + ledCol * (hexContrib + eGlow * 0.3);
                float  alpha = saturate(_GlassOpacity + hexContrib * 1.5 + eGlow * 0.22);

                return float4(col, alpha);
            }
            ENDHLSL
        }
    }
}
