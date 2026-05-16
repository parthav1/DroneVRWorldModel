import type { Config } from "tailwindcss";

export default {
  content: ["./index.html", "./src/**/*.{ts,tsx}"],
  theme: {
    extend: {
      colors: {
        void: "#030407",
        ink: "#070b13",
        panel: "#0d1422",
        cyan: "#22d3ee",
        lime: "#a3e635",
        ember: "#f97316",
        magenta: "#f472b6",
      },
      boxShadow: {
        glow: "0 0 80px rgba(34, 211, 238, 0.22)",
        ember: "0 0 70px rgba(249, 115, 22, 0.22)",
      },
      animation: {
        float: "float 8s ease-in-out infinite",
        pulseGlow: "pulseGlow 4s ease-in-out infinite",
        drift: "drift 22s linear infinite",
        scan: "scan 5s ease-in-out infinite",
        reveal: "reveal 0.8s ease-out both",
      },
      keyframes: {
        float: {
          "0%, 100%": { transform: "translateY(0px)" },
          "50%": { transform: "translateY(-18px)" },
        },
        pulseGlow: {
          "0%, 100%": { opacity: "0.45", transform: "scale(1)" },
          "50%": { opacity: "0.95", transform: "scale(1.06)" },
        },
        drift: {
          "0%": { transform: "translate3d(-4%, 0, 0)" },
          "100%": { transform: "translate3d(4%, 0, 0)" },
        },
        scan: {
          "0%, 100%": { transform: "translateY(-25%)", opacity: "0" },
          "50%": { transform: "translateY(110%)", opacity: "0.55" },
        },
        reveal: {
          "0%": { opacity: "0", transform: "translateY(24px)" },
          "100%": { opacity: "1", transform: "translateY(0)" },
        },
      },
    },
  },
  plugins: [],
} satisfies Config;
