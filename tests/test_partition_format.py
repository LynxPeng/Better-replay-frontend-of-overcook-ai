"""Schema 2 is inspectable without installing the optional HuCooked v3 core."""
import copy
import json
from pathlib import Path

import pytest
from better_overcooked_replay import validate_replay


def sample():
    path = Path(__file__).resolve().parents[1] / 'unity/OvercookedViewer/Assets/StreamingAssets/Samples/partition_v3_sync.ocrep.json'
    return json.loads(path.read_text())


def test_schema2_offline_validation():
    data = sample()
    assert validate_replay(data) is data


@pytest.mark.parametrize('problem', ['missing_field', 'heat', 'terrain', 'info_shape', 'negative_time', 'info_type', 'missing_count'])
def test_invalid_schema2_is_a_value_error(problem):
    data = sample()
    if problem == 'missing_field': del data['frames'][0]['state']['partition']['next_id']
    if problem == 'heat': data['frames'][0]['state']['partition']['heat'][0] = 9
    if problem == 'terrain': data['header']['terrain'][0] = ' '*13
    if problem == 'info_shape': data['segments'][0]['partition_info']['expired'] = [False]
    if problem == 'negative_time': data['frames'][0]['time'] = -.5
    if problem == 'info_type': data['segments'][0]['partition_info']['age'] = ['a']*3
    if problem == 'missing_count': del data['segments'][0]['partition_info']['sync_successes']
    with pytest.raises(ValueError): validate_replay(data)


def test_per_recipe_rewards_in_offline_file():
    data = sample()
    data['header']['complete'] = False
    cfg=data['header']['partition_config']
    cfg.update(delivery_reward=None,recipe_rewards=[20,10,50])
    for i,step in enumerate(data['segments']):
        info=step['partition_info']
        step['sparse_reward']=sum(cfg['recipe_rewards'][recipe] for recipe,done in zip(info['recipe'],info['delivered']) if done)
        data['frames'][i+1]['score']=data['frames'][i]['score']+step['sparse_reward']
    assert validate_replay(data) is data
