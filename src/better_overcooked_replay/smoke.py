import json
from pathlib import Path
import numpy as np


def train_smoke(output_dir, total_timesteps=4096):
    import torch
    from stable_baselines3 import PPO
    from stable_baselines3.common.env_checker import check_env
    from .training import SingleChefEnv
    torch.set_num_threads(4)
    destination = Path(output_dir)
    destination.mkdir(parents=True, exist_ok=True)
    env = SingleChefEnv()
    check_env(env)
    model = PPO('CnnPolicy',env,seed=0,device='cpu',n_steps=256,batch_size=64,n_epochs=4,
                ent_coef=0.01,verbose=0)
    before = torch.cat([p.detach().flatten().clone() for p in model.policy.parameters()])
    model.learn(total_timesteps=total_timesteps)
    after = torch.cat([p.detach().flatten() for p in model.policy.parameters()])
    delta = torch.linalg.vector_norm(after-before).item()
    losses = {k:float(v) for k,v in model.logger.name_to_value.items()
              if k.startswith('train/') and isinstance(v,(int,float,np.number))}
    if not delta > 0 or not all(np.isfinite(v) for v in losses.values()):
        raise RuntimeError('PPO failed finite parameter-update verification')
    model.save(destination / 'policy')
    loaded = PPO.load(destination / 'policy', device='cpu')
    env.close()
    evaluation = SingleChefEnv(record_dir=destination/'evaluation')
    obs,_ = evaluation.reset(seed=0)
    sparse_score = 0
    for _ in range(400):
        action,_ = loaded.predict(obs,deterministic=True)
        obs,_,terminated,truncated,info = evaluation.step(int(action))
        sparse_score += info['sparse_reward']
        if terminated or truncated: break
    evaluation.close()
    result = dict(timesteps=model.num_timesteps,parameter_delta=delta,losses=losses,
                  eval_sparse_score=sparse_score,eval_frames=len(evaluation.last_replay['frames']),
                  complete=evaluation.last_replay['header']['complete'],device='cpu')
    (destination/'report.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
    return result
