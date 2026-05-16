import { useEffect, useMemo, useState } from "react";

type Metric = {
  value: string;
  label: string;
  tone: string;
};

type Figure = {
  src: string;
  title: string;
  caption: string;
};

const asset = (path: string) => `${import.meta.env.BASE_URL}${path.replace(/^\//, "")}`;

const metrics: Metric[] = [
  { value: "7", label: "3D scenes", tone: "text-cyan-200" },
  { value: "70", label: "random-walk flights", tone: "text-lime-200" },
  { value: "500", label: "frames per run", tone: "text-orange-200" },
  { value: "~30GB", label: "generated dataset", tone: "text-pink-200" },
];

const pipeline = [
  "DJI aerial capture",
  "WebODM reconstruction",
  "GLB import into Unity",
  "Scripted + VR flight",
  "Frames + poses.csv",
  "VO evaluation",
];

const stateOfArt = [
  {
    title: "TartanAir",
    body: "A synthetic aerial simulation dataset with diverse environments and weather. It is valuable, but it is still generated from synthetic worlds rather than drone-reconstructed real sites.",
  },
  {
    title: "XVO",
    body: "A semi-self-supervised RGB-only visual odometry model. We use it as the baseline/fine-tuning target for testing whether our generated data can support aerial VO.",
  },
  {
    title: "What is missing",
    body: "A customizable simulator that supports cross-altitude drone data, real-world reconstruction artifacts, controllable flight policies, and exact ground-truth pose.",
  },
];

const roleCards = [
  {
    name: "Parthav",
    work: ["Drone data collection", "3D reconstruction pipeline", "Data collection simulator", "Unity development"],
  },
  {
    name: "Kaushal",
    work: ["VO model fine-tuning", "VR integration", "Model evaluation", "Manual collection workflow"],
  },
];

const tuningRows = [
  ["XVO baseline", "29.12", "31.69", "0.946"],
  ["Train VO head", "22.28", "30.47", "0.534"],
  ["Train VO head + scale loss", "9.28", "30.69", "0.277"],
  ["Train VO + depth head", "10.34", "30.76", "0.288"],
];

const figures: Figure[] = [
  {
    src: asset("figures/ground_truth_random_walk_only.png"),
    title: "Ground-truth random walk",
    caption: "Unity records exact camera pose while the drone moves through a reconstructed scene.",
  },
  {
    src: asset("figures/finetuned_trajectory_vs_ground_truth.png"),
    title: "Fine-tuned VO vs. ground truth",
    caption: "Predicted motion is compared against Unity ground truth after standard similarity alignment.",
  },
  {
    src: asset("figures/finetuned_vs_xvo_baseline_trajectory.png"),
    title: "Fine-tuned model vs. XVO baseline",
    caption: "The same trajectory can be compared against a classical ORB/XVO baseline.",
  },
];

const sceneCards = [
  {
    src: asset("realDjiExample.png"),
    kicker: "Capture",
    title: "Real Drone Survey",
    body: "A DJI Matrice-style workflow captures the real site before reconstruction.",
  },
  {
    src: asset("3dExample.png"),
    kicker: "Reconstruction",
    title: "Photogrammetry Mesh",
    body: "WebODM converts aerial imagery into GLB environments that preserve real texture and structure.",
  },
  {
    src: asset("simBoxExample.png"),
    kicker: "Simulation",
    title: "Bounded Flight Volume",
    body: "The simulator fits a flight box above the reconstructed environment for controlled data generation.",
  },
];

function useScrollProgress() {
  const [progress, setProgress] = useState(0);

  useEffect(() => {
    const update = () => {
      const max = document.documentElement.scrollHeight - window.innerHeight;
      setProgress(max <= 0 ? 0 : window.scrollY / max);
    };

    update();
    window.addEventListener("scroll", update, { passive: true });
    window.addEventListener("resize", update);
    return () => {
      window.removeEventListener("scroll", update);
      window.removeEventListener("resize", update);
    };
  }, []);

  return progress;
}

function GlowBackground() {
  return (
    <div aria-hidden className="pointer-events-none fixed inset-0 -z-10 overflow-hidden bg-void">
      <div className="absolute inset-0 bg-[radial-gradient(circle_at_20%_10%,rgba(34,211,238,0.18),transparent_32%),radial-gradient(circle_at_80%_0%,rgba(249,115,22,0.16),transparent_34%),linear-gradient(180deg,#030407_0%,#07101d_45%,#020308_100%)]" />
      <div className="absolute left-1/2 top-0 h-[42rem] w-[42rem] -translate-x-1/2 rounded-full bg-cyan-400/10 blur-3xl animate-pulseGlow" />
      <div className="absolute bottom-[-18rem] right-[-10rem] h-[40rem] w-[40rem] rounded-full bg-ember/10 blur-3xl animate-float" />
      <div className="grid-mask absolute inset-0 opacity-[0.16]" />
      <div className="starfield absolute inset-0 opacity-70" />
    </div>
  );
}

function SectionLabel({ children }: { children: string }) {
  return (
    <div className="mb-5 inline-flex items-center gap-3 rounded-full border border-white/10 bg-white/[0.04] px-4 py-2 text-xs font-bold uppercase tracking-[0.35em] text-cyan-100/80">
      <span className="h-1.5 w-1.5 rounded-full bg-cyan-300 shadow-[0_0_18px_rgba(34,211,238,0.8)]" />
      {children}
    </div>
  );
}

function GlassCard({
  children,
  className = "",
}: {
  children: React.ReactNode;
  className?: string;
}) {
  return (
    <div className={`group relative overflow-hidden rounded-[2rem] border border-white/10 bg-white/[0.055] p-6 shadow-2xl backdrop-blur-xl transition duration-500 hover:-translate-y-1 hover:border-cyan-300/40 hover:bg-white/[0.075] ${className}`}>
      <div className="absolute inset-x-0 top-0 h-px bg-gradient-to-r from-transparent via-cyan-200/70 to-transparent opacity-60" />
      <div className="relative z-10">{children}</div>
    </div>
  );
}

function MediaFrame({
  children,
  className = "",
}: {
  children: React.ReactNode;
  className?: string;
}) {
  return (
    <div className={`relative overflow-hidden rounded-[2rem] border border-white/10 bg-black shadow-glow ${className}`}>
      <div className="absolute inset-0 z-10 pointer-events-none ring-1 ring-inset ring-white/10" />
      <div className="scanline absolute inset-x-0 top-0 z-20 h-24 bg-gradient-to-b from-cyan-200/0 via-cyan-200/10 to-cyan-200/0" />
      {children}
    </div>
  );
}

function App() {
  const progress = useScrollProgress();

  const navItems = useMemo(
    () => [
      ["Motivation", "#motivation"],
      ["System", "#system"],
      ["Dataset", "#dataset"],
      ["VR", "#vr"],
      ["Results", "#results"],
    ],
    [],
  );

  return (
    <>
      <GlowBackground />
      <div
        className="fixed left-0 top-0 z-50 h-1 bg-gradient-to-r from-cyan-300 via-lime-200 to-orange-300 transition-[width]"
        style={{ width: `${progress * 100}%` }}
      />
      <header className="sticky top-0 z-40 border-b border-white/10 bg-black/45 backdrop-blur-2xl">
        <nav className="mx-auto flex max-w-7xl items-center justify-between px-5 py-4">
          <a href="#top" className="flex items-center gap-3 font-black tracking-tight text-white">
            <span className="grid h-9 w-9 place-items-center rounded-xl bg-gradient-to-br from-cyan-300 to-lime-200 text-black shadow-glow">X</span>
            <span className="hidden sm:block">DroneVO World Model</span>
          </a>
          <div className="hidden items-center gap-2 md:flex">
            {navItems.map(([label, href]) => (
              <a key={href} href={href} className="rounded-full px-4 py-2 text-sm text-white/70 transition hover:bg-white/10 hover:text-white">
                {label}
              </a>
            ))}
          </div>
        </nav>
      </header>

      <main id="top">
        <section className="relative mx-auto grid min-h-[92vh] max-w-7xl items-center gap-12 px-5 py-20 lg:grid-cols-[1.02fr_0.98fr]">
          <div className="animate-reveal">
            <div className="mb-6 inline-flex rounded-full border border-cyan-200/20 bg-cyan-200/10 px-4 py-2 text-sm font-semibold text-cyan-100">
              CMSC731 Final Project · Reconstruction-based drone simulation for VO
            </div>
            <h1 className="max-w-5xl text-6xl font-black leading-[0.9] tracking-[-0.08em] text-white sm:text-7xl lg:text-8xl">
              Immersive Drone Simulator for Navigation & Localization
            </h1>
            <p className="mt-8 max-w-2xl text-xl leading-8 text-slate-300">
              We convert drone-captured real environments into Unity flight worlds, generate controllable camera trajectories, and fine-tune/evaluate visual odometry on data that preserves real photogrammetry texture, geometry, and reconstruction artifacts.
            </p>
            <div className="mt-9 flex flex-wrap gap-3">
              <a href="#results" className="rounded-full bg-white px-6 py-3 text-sm font-black text-black transition hover:scale-105 hover:bg-cyan-100">
                See results
              </a>
              <a href="#system" className="rounded-full border border-white/15 px-6 py-3 text-sm font-black text-white transition hover:scale-105 hover:border-cyan-200/60 hover:bg-white/10">
                View pipeline
              </a>
            </div>
          </div>

          <div className="relative animate-reveal [animation-delay:160ms]">
            <div className="absolute -inset-4 rounded-[2.5rem] bg-gradient-to-br from-cyan-300/20 via-transparent to-orange-300/20 blur-2xl" />
            <MediaFrame>
              <video className="h-full w-full object-cover" src={asset("simDemoDay.mp4")} autoPlay muted loop playsInline />
            </MediaFrame>
            <div className="absolute -bottom-5 left-6 right-6 grid grid-cols-2 gap-3 sm:grid-cols-4">
              {metrics.map((item) => (
                <div key={item.label} className="rounded-2xl border border-white/10 bg-black/70 p-4 text-center backdrop-blur-xl">
                  <div className={`text-2xl font-black ${item.tone}`}>{item.value}</div>
                  <div className="mt-1 text-[0.68rem] uppercase tracking-widest text-white/55">{item.label}</div>
                </div>
              ))}
            </div>
          </div>
        </section>

        <section id="motivation" className="mx-auto max-w-7xl px-5 py-24">
          <SectionLabel>Why this exists</SectionLabel>
          <div className="grid gap-6 lg:grid-cols-[0.95fr_1.05fr]">
            <div>
              <h2 className="text-4xl font-black tracking-[-0.05em] text-white sm:text-6xl">
                Realistic drone VO data is hard to get.
              </h2>
              <p className="mt-6 text-lg leading-8 text-slate-300">
                Mid-to-high altitude drone visual odometry is still underdeveloped compared with car, indoor, and ground-robot VO. One practical blocker is a lack of datasets and benchmarks for models operating over construction sites, farms, parks, neighborhoods, and other drone-survey environments.
              </p>
            </div>
            <GlassCard className="grid content-between">
              <div className="text-2xl font-black text-white">Our position</div>
              <p className="mt-4 text-lg leading-8 text-slate-300">
                Drone-based 3D reconstruction is already widely used in construction, agriculture, mapping, and inspection. Our question is whether those reconstructions can also become fast, controllable simulation worlds for generating VO training and testing data.
              </p>
              <div className="mt-8 grid gap-3 sm:grid-cols-3">
                {["Cross altitude", "Exact pose", "Real texture"].map((item) => (
                  <div key={item} className="rounded-2xl bg-white/[0.06] p-4 text-sm font-bold text-cyan-100">
                    {item}
                  </div>
                ))}
              </div>
            </GlassCard>
          </div>
          <div className="mt-10 grid gap-6 lg:grid-cols-3">
            {stateOfArt.map((item) => (
              <GlassCard key={item.title}>
                <h3 className="text-2xl font-black text-white">{item.title}</h3>
                <p className="mt-4 leading-7 text-slate-300">{item.body}</p>
              </GlassCard>
            ))}
          </div>
        </section>

        <section id="system" className="mx-auto max-w-7xl px-5 py-24">
          <SectionLabel>System overview</SectionLabel>
          <div className="mb-10 flex flex-col justify-between gap-5 lg:flex-row lg:items-end">
            <h2 className="max-w-4xl text-4xl font-black tracking-[-0.05em] text-white sm:text-6xl">
              From DJI imagery to VO-style datasets.
            </h2>
            <p className="max-w-xl text-lg leading-8 text-slate-300">
              The simulator packages each run as synchronized rendered frames, Unity poses, and trajectory logs. The flight volume is placed above each reconstruction, roughly 10 feet above the tallest obstacle with a 40-foot vertical operating band.
            </p>
          </div>

          <div className="grid gap-4 md:grid-cols-3 lg:grid-cols-6">
            {pipeline.map((step, index) => (
              <GlassCard key={step} className="min-h-40">
                <div className="mb-8 text-sm font-black text-cyan-200">0{index + 1}</div>
                <div className="text-lg font-black text-white">{step}</div>
              </GlassCard>
            ))}
          </div>

          <div className="mt-10 grid gap-6 lg:grid-cols-3">
            {sceneCards.map((card) => (
              <GlassCard key={card.title} className="p-0">
                <img className="h-56 w-full object-cover" src={card.src} alt={card.title} />
                <div className="p-6">
                  <div className="text-xs font-black uppercase tracking-[0.3em] text-cyan-200/80">{card.kicker}</div>
                  <h3 className="mt-3 text-2xl font-black text-white">{card.title}</h3>
                  <p className="mt-3 leading-7 text-slate-300">{card.body}</p>
                </div>
              </GlassCard>
            ))}
          </div>
          <div className="mt-10 grid gap-6 lg:grid-cols-2">
            {roleCards.map((role) => (
              <GlassCard key={role.name}>
                <div className="text-sm font-black uppercase tracking-[0.3em] text-cyan-200/80">Project role</div>
                <h3 className="mt-3 text-3xl font-black text-white">{role.name}</h3>
                <div className="mt-5 grid gap-3 sm:grid-cols-2">
                  {role.work.map((item) => (
                    <div key={item} className="rounded-2xl bg-white/[0.06] p-4 text-sm font-bold text-slate-200">
                      {item}
                    </div>
                  ))}
                </div>
              </GlassCard>
            ))}
          </div>
        </section>

        <section id="dataset" className="mx-auto max-w-7xl px-5 py-24">
          <SectionLabel>Data generation</SectionLabel>
          <div className="grid gap-8 lg:grid-cols-[0.9fr_1.1fr]">
            <div>
              <h2 className="text-4xl font-black tracking-[-0.05em] text-white sm:text-6xl">
                Automated flights across multiple real-world reconstructions.
              </h2>
              <p className="mt-6 text-lg leading-8 text-slate-300">
                Batch runs execute random-walk policies inside fitted scene bounds. Across 7 reconstructed scenes, each run records 500 frames at one frame per second, synchronized ground-truth pose, and trajectory metadata for VO training and evaluation.
              </p>
              <div className="mt-8 grid gap-4 sm:grid-cols-2">
                {[
                  ["Policy", "Constrained random walk"],
                  ["Capture rate", "1 frame / second"],
                  ["Batch scale", "7 scenes · 70 runs"],
                  ["Camera", "40° survey pitch"],
                  ["Runtime", "~9.7 hours"],
                  ["Storage", "~30GB collected"],
                ].map(([label, value]) => (
                  <div key={label} className="rounded-3xl border border-white/10 bg-white/[0.05] p-5">
                    <div className="text-sm uppercase tracking-[0.25em] text-white/45">{label}</div>
                    <div className="mt-2 text-xl font-black text-white">{value}</div>
                  </div>
                ))}
              </div>
            </div>
            <MediaFrame>
              <img src={asset("dayExample.png")} alt="Unity daytime simulation capture" className="h-full w-full object-cover" />
            </MediaFrame>
          </div>

          <div className="mt-8 grid gap-6 md:grid-cols-2">
            <GlassCard className="p-0">
              <img src={asset("nightExample.png")} alt="Night weather mode" className="h-72 w-full object-cover" />
              <div className="p-6">
                <h3 className="text-2xl font-black text-white">Lighting variants</h3>
                <p className="mt-3 leading-7 text-slate-300">Night mode changes ambient light, tint, and sky response to test how aerial VO behaves when visual contrast and color cues shift.</p>
              </div>
            </GlassCard>
            <GlassCard className="p-0">
              <img src={asset("rainExample.png")} alt="Rain weather mode" className="h-72 w-full object-cover" />
              <div className="p-6">
                <h3 className="text-2xl font-black text-white">Weather stress tests</h3>
                <p className="mt-3 leading-7 text-slate-300">Rain, darker exposure, and future wind/camera shake variants give us a path toward robustness testing beyond clean daytime flights.</p>
              </div>
            </GlassCard>
          </div>
        </section>

        <section id="vr" className="mx-auto max-w-7xl px-5 py-24">
          <SectionLabel>Manual VR collection</SectionLabel>
          <div className="grid gap-8 lg:grid-cols-[1.15fr_0.85fr]">
            <MediaFrame>
              <video className="h-full w-full object-cover" src={asset("vrDemoDay.mov")} autoPlay muted loop playsInline />
            </MediaFrame>
            <div>
              <h2 className="text-4xl font-black tracking-[-0.05em] text-white sm:text-6xl">
                Human-in-the-loop FPV data collection.
              </h2>
              <p className="mt-6 text-lg leading-8 text-slate-300">
                Automated policies are controlled, but human pilots choose viewpoints differently. The VR mode turns the simulator into an immersive FPV flight tool using Quest Pro joystick controls, allowing users to collect custom high-altitude trajectories while the system records exact pose and images.
              </p>
              <div className="mt-8 space-y-4">
                {[
                  "Quest joystick controls mirror familiar FPV-style motion.",
                  "Users can collect targeted trajectories around difficult scene structures and repeated textures.",
                  "Manual flights support robustness testing, pilot-assisted dataset generation, and future user studies.",
                ].map((item) => (
                  <div key={item} className="flex gap-4 rounded-2xl border border-white/10 bg-white/[0.05] p-4 text-slate-200">
                    <span className="mt-1 h-2.5 w-2.5 shrink-0 rounded-full bg-lime-300 shadow-[0_0_16px_rgba(163,230,53,0.8)]" />
                    <span>{item}</span>
                  </div>
                ))}
              </div>
            </div>
          </div>
        </section>

        <section id="results" className="mx-auto max-w-7xl px-5 py-24">
          <SectionLabel>VO evaluation</SectionLabel>
          <div className="mb-10 flex flex-col justify-between gap-5 lg:flex-row lg:items-end">
            <h2 className="max-w-4xl text-4xl font-black tracking-[-0.05em] text-white sm:text-6xl">
              Ground truth lets us evaluate drift, scale, and trajectory shape.
            </h2>
            <p className="max-w-xl text-lg leading-8 text-slate-300">
              We compare model predictions against Unity ground truth and a feature-based XVO/ORB baseline. Fine-tuning improves translation and scale substantially, while rotation remains difficult for high-altitude aerial imagery.
            </p>
          </div>
          <GlassCard className="mb-6 overflow-x-auto">
            <div className="mb-5 flex flex-col gap-2 md:flex-row md:items-end md:justify-between">
              <div>
                <h3 className="text-2xl font-black text-white">XVO fine-tuning summary</h3>
                <p className="mt-2 text-slate-300">Lower is better. Adding scale-aware loss gave the strongest translation and scale improvement.</p>
              </div>
              <div className="rounded-full border border-orange-200/20 bg-orange-200/10 px-4 py-2 text-sm font-bold text-orange-100">
                Rotation remains the hardest failure mode
              </div>
            </div>
            <table className="w-full min-w-[720px] border-separate border-spacing-y-2 text-left">
              <thead className="text-xs uppercase tracking-[0.25em] text-white/45">
                <tr>
                  <th className="px-4 py-2">Experiment</th>
                  <th className="px-4 py-2">Translation error</th>
                  <th className="px-4 py-2">Rotation error</th>
                  <th className="px-4 py-2">Scale error</th>
                </tr>
              </thead>
              <tbody>
                {tuningRows.map((row) => (
                  <tr key={row[0]} className="bg-white/[0.055] text-slate-100">
                    <td className="rounded-l-2xl px-4 py-4 font-black">{row[0]}</td>
                    <td className="px-4 py-4">{row[1]}</td>
                    <td className="px-4 py-4">{row[2]}°</td>
                    <td className="rounded-r-2xl px-4 py-4">{row[3]}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </GlassCard>
          <div className="grid gap-6">
            {figures.map((figure) => (
              <GlassCard key={figure.src} className="p-0">
                <img src={figure.src} alt={figure.title} className="w-full object-cover" />
                <div className="p-6">
                  <h3 className="text-2xl font-black text-white">{figure.title}</h3>
                  <p className="mt-2 leading-7 text-slate-300">{figure.caption}</p>
                </div>
              </GlassCard>
            ))}
          </div>
        </section>

        <section className="mx-auto max-w-7xl px-5 py-24">
          <SectionLabel>Contributions</SectionLabel>
          <div className="grid gap-6 lg:grid-cols-3">
            {[
              {
                title: "Reconstruction-based simulator",
                body: "Imported drone-generated GLB reconstructions into Unity and built a reusable flight/data capture framework around them.",
              },
              {
                title: "Automated dataset generation",
                body: "Implemented controlled random-walk batch capture with synchronized frames, poses, trajectory logs, and VO-ready exports.",
              },
              {
                title: "Interactive VR extension",
                body: "Added a manual FPV-style mode for human-guided data collection and future navigation/localization studies.",
              },
            ].map((item) => (
              <GlassCard key={item.title}>
                <h3 className="text-2xl font-black text-white">{item.title}</h3>
                <p className="mt-4 leading-7 text-slate-300">{item.body}</p>
              </GlassCard>
            ))}
          </div>

          <div className="mt-10 rounded-[2rem] border border-white/10 bg-gradient-to-br from-cyan-300/12 via-white/[0.04] to-orange-300/12 p-8">
            <div className="grid gap-8 lg:grid-cols-[0.7fr_1.3fr] lg:items-center">
              <h2 className="text-4xl font-black tracking-[-0.05em] text-white">What comes next?</h2>
              <div className="grid gap-4 md:grid-cols-2">
                {[
                  "Add richer weather, lighting, texture packs, and wind-driven camera shake",
                  "Include depth maps, IMU data, and realistic sensor noise",
                  "Improve sim-to-real by comparing raw DJI imagery against simulator frames",
                  "Develop aerial VO models with landmark focus, segmentation, temporal awareness, and uncertainty",
                ].map((item) => (
                  <div key={item} className="rounded-2xl bg-black/35 p-5 text-slate-200">
                    {item}
                  </div>
                ))}
              </div>
            </div>
          </div>
        </section>
      </main>

      <footer className="border-t border-white/10 px-5 py-10">
        <div className="mx-auto flex max-w-7xl flex-col gap-4 text-sm text-white/55 md:flex-row md:items-center md:justify-between">
          <div>Immersive Drone Simulator for Navigation and Localization</div>
          <div>Kaushal Janga · Parthav Poudel · University of Maryland</div>
        </div>
      </footer>
    </>
  );
}

export default App;
